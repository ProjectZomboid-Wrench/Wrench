package io.pznet.wrench;

import java.util.ArrayList;
import java.util.List;

/** Small ids for Java classes, so Wrench.Host can cache class names and look one up with a single int call. */
public final class WrenchClasses {
    private static final List<String> NAMES = new ArrayList<>();

    // ClassValue may compute a class twice under a race; the losing id is never handed out, so it's only a wasted slot.
    private static final ClassValue<Integer> IDS = new ClassValue<>() {
        @Override
        protected Integer computeValue(Class<?> type) {
            synchronized (NAMES) {
                NAMES.add(type.getName());
                return NAMES.size() - 1;
            }
        }
    };

    private WrenchClasses() {
    }

    public static int id(Object value) {
        return IDS.get(value.getClass());
    }

    public static String name(int id) {
        synchronized (NAMES) {
            return NAMES.get(id);
        }
    }
}
