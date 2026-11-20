package io.pznet.wrench;

import static net.bytebuddy.matcher.ElementMatchers.named;

import java.lang.instrument.Instrumentation;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import net.bytebuddy.agent.builder.AgentBuilder;
import net.bytebuddy.asm.Advice;

/** Puts advice on batch boundaries and flush points, so events fired inside a boundary reach .NET handlers in one call. */
final class EventBatches {
    /** Opens a batch in {@code method} ("class#method"); delivers it at the first {@code flushBefore} reached, or on return. */
    record Boundary(String method, List<String> flushBefore) {
    }

    // Flush before the chunk reaches erosion, collision and pathfinding, as per-square handlers ran; see CLAUDE.md.
    static final Map<String, Boundary> BOUNDARIES = Map.of(
            "LoadGridsquare", new Boundary("zombie.iso.IsoChunk#doLoadGridsquare", List.of(
                    "zombie.erosion.ErosionMain#ChunkLoaded",
                    "zombie.globalObjects.SGlobalObjects#chunkLoaded",
                    "zombie.ReanimatedPlayers#addReanimatedPlayersToChunk")));

    private EventBatches() {
    }

    /** The batched events a flush point delivers, by "class#method". */
    static Set<String> eventsFlushedAt(String method) {
        var events = new HashSet<String>();
        for (var entry : BOUNDARIES.entrySet()) {
            if (entry.getValue().flushBefore().contains(method))
                events.add(entry.getKey());
        }
        return events;
    }

    static void install(Instrumentation instrumentation) {
        transform(instrumentation, BOUNDARIES.values().stream().map(Boundary::method), BoundaryAdvice.class);
        transform(instrumentation, BOUNDARIES.values().stream().flatMap(b -> b.flushBefore().stream()), FlushAdvice.class);
    }

    private static void transform(Instrumentation instrumentation, Stream<String> methods, Class<?> advice) {
        var byClass = methods.distinct().map(m -> m.split("#", 2))
                .collect(Collectors.groupingBy(m -> m[0], Collectors.mapping(m -> m[1], Collectors.toSet())));
        for (var entry : byClass.entrySet()) {
            var names = entry.getValue();
            new AgentBuilder.Default()
                    .disableClassFormatChanges()
                    .with(AgentBuilder.Listener.StreamWriting.toSystemError().withErrorsOnly())
                    .type(named(entry.getKey()))
                    .transform((builder, type, loader, module, domain) -> builder.visit(
                            Advice.to(advice).on(m -> names.contains(m.getName()))))
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

    static final class FlushAdvice {
        private FlushAdvice() {
        }

        @Advice.OnMethodEnter(suppress = Throwable.class)
        static void enter(@Advice.Origin("#t") String type, @Advice.Origin("#m") String method) {
            WrenchEvents.flushBatch(type + "#" + method);
        }
    }
}
