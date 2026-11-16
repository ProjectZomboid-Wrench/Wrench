package io.pznet.wrench;

import static net.bytebuddy.matcher.ElementMatchers.named;
import static net.bytebuddy.matcher.ElementMatchers.takesArguments;

import java.lang.instrument.Instrumentation;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;
import net.bytebuddy.implementation.bytecode.assign.Assigner;
import zombie.gameStates.GameStateMachine;

/**
 * Puts {@link WrenchPromptState} in front of the main menu using B42's own suspend/return: a state whose update
 * returns Yield is pushed aside for whatever its redirectState returns, and re-entered once that state finishes.
 * MainScreenState doesn't override redirectState, so that half hooks the base GameState.
 */
final class PermissionPromptHook {
    private PermissionPromptHook() {
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
            var prompt = WrenchPermissions.takeYieldRedirect(self);
            if (prompt != null)
                result = prompt;
        }
    }
}
