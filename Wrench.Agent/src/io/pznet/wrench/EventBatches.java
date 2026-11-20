package io.pznet.wrench;

import static net.bytebuddy.matcher.ElementMatchers.named;

import java.lang.instrument.Instrumentation;
import java.util.Map;
import java.util.stream.Collectors;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;

/** Puts advice on each batch boundary method, so events dispatched inside it reach .NET batch handlers in one call. */
final class EventBatches {
    // Event -> "class#method" whose return flushes its batch. Dispatches outside a boundary are delivered one at a time.
    static final Map<String, String> BOUNDARIES = Map.of(
            "LoadGridsquare", "zombie.iso.IsoChunk#doLoadGridsquare");

    private EventBatches() {
    }

    static void install(Instrumentation instrumentation) {
        var byClass = BOUNDARIES.values().stream().map(b -> b.split("#", 2))
                .collect(Collectors.groupingBy(b -> b[0], Collectors.mapping(b -> b[1], Collectors.toSet())));
        for (var entry : byClass.entrySet()) {
            var methods = entry.getValue();
            new AgentBuilder.Default()
                    .disableClassFormatChanges()
                    .with(AgentBuilder.Listener.StreamWriting.toSystemError().withErrorsOnly())
                    .type(named(entry.getKey()))
                    .transform((builder, type, loader, module, domain) -> builder.visit(
                            Advice.to(BoundaryAdvice.class).on(m -> methods.contains(m.getName()))))
                    .installOn(instrumentation);
        }
    }

    static final class BoundaryAdvice {
        private BoundaryAdvice() {
        }

        // Inlined into the boundary method; suppress keeps a Wrench failure from breaking the game's code.
        @Advice.OnMethodEnter(suppress = Throwable.class)
        static void enter() {
            WrenchEvents.openBatch();
        }

        @Advice.OnMethodExit(onThrowable = Throwable.class, suppress = Throwable.class)
        static void exit() {
            WrenchEvents.closeBatch();
        }
    }
}
