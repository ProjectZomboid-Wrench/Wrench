package io.pznet.wrench;

import static net.bytebuddy.matcher.ElementMatchers.named;
import static net.bytebuddy.matcher.ElementMatchers.takesArguments;

import java.lang.instrument.Instrumentation;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;
import zombie.gameStates.GameStateMachine;

/**
 * Wrench's own game states, slotted in through PZ's state machine rather than by editing its state list. A state that
 * finishes (Continue) or yields (Yield) is asked for a redirectState before the machine moves on; neither TISLogoState
 * nor MainScreenState overrides it, so one hook on the base GameState covers both:
 * <ul>
 * <li>TISLogoState finishing is redirected once to {@link WrenchLogoState}, after which the machine carries on to the
 * next state as usual.</li>
 * <li>MainScreenState is made to yield to {@link WrenchPromptState} while mods wait for permission, and is
 * re-entered once the prompt finishes.</li>
 * </ul>
 */
public final class GameStateHooks {
    private static boolean splashShown;

    private GameStateHooks() {
    }

    static void install(Instrumentation instrumentation) {
        new AgentBuilder.Default()
                .disableClassFormatChanges()
                .with(AgentBuilder.Listener.StreamWriting.toSystemError().withErrorsOnly())
                .type(named("zombie.gameStates.MainScreenState"))
                .transform((builder, type, loader, module, domain) ->
                        builder.visit(Advice.to(UpdateAdvice.class).on(named("update").and(takesArguments(0)))))
                .type(named("zombie.gameStates.GameState"))
                .transform((builder, type, loader, module, domain) ->
                        builder.visit(Advice.to(RedirectAdvice.class).on(named("redirectState").and(takesArguments(0)))))
                .installOn(instrumentation);
    }

    static final class UpdateAdvice {
        private UpdateAdvice() {
        }

        @Advice.OnMethodExit(suppress = Throwable.class)
        static void exit(@Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object result) {
            if (result == GameStateMachine.StateAction.Remain && WrenchPermissions.shouldYield())
                result = GameStateMachine.StateAction.Yield;
        }
    }

    static final class RedirectAdvice {
        private RedirectAdvice() {
        }

        @Advice.OnMethodExit(suppress = Throwable.class)
        static void exit(@Advice.This Object self,
                         @Advice.Return(readOnly = false, typing = Assigner.Typing.DYNAMIC) Object result) {
            var redirect = GameStateHooks.redirect(self);
            if (redirect != null)
                result = redirect;
        }
    }

    public static synchronized Object redirect(Object state) {
        var prompt = WrenchPermissions.takeYieldRedirect(state);
        if (prompt != null)
            return prompt;
        if (!splashShown && state.getClass().getName().equals("zombie.gameStates.TISLogoState")) {
            splashShown = true;
            return new WrenchLogoState();
        }
        return null;
    }
}
