package io.pznet.wrench;

import static net.bytebuddy.matcher.ElementMatchers.named;
import static net.bytebuddy.matcher.ElementMatchers.returns;
import static net.bytebuddy.matcher.ElementMatchers.takesArguments;

import java.lang.instrument.Instrumentation;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;

/**
 * Every Lua event dispatch in PZ ends in {@code zombie.Lua.Event.trigger(KahluaTable, LuaCaller, Object[])},
 * including the queued and triggerEventGarbage/Unique paths, so one advice on it sees them all: on entry for
 * Wrench's hooks, on exit for mod handlers.
 */
final class LuaEventHook {
    private LuaEventHook() {
    }

    static void install(Instrumentation instrumentation) {
        new AgentBuilder.Default()
                .disableClassFormatChanges()
                .with(AgentBuilder.Listener.StreamWriting.toSystemError().withErrorsOnly())
                .type(named("zombie.Lua.Event"))
                .transform((builder, type, loader, module, domain) -> builder.visit(
                        Advice.to(TriggerAdvice.class).on(named("trigger").and(takesArguments(3)).and(returns(boolean.class)))))
                .installOn(instrumentation);
    }

    static final class TriggerAdvice {
        private TriggerAdvice() {
        }

        // Inlined into Event.trigger; suppress keeps a Wrench failure from breaking the game's dispatch.
        @Advice.OnMethodEnter(suppress = Throwable.class)
        static void enter(@Advice.FieldValue("name") String name, @Advice.Argument(2) Object[] args) {
            WrenchEvents.dispatch(name, args, false);
        }

        @Advice.OnMethodExit(onThrowable = Throwable.class, suppress = Throwable.class)
        static void exit(@Advice.FieldValue("name") String name, @Advice.Argument(2) Object[] args) {
            WrenchEvents.dispatch(name, args, true);
        }
    }
}
