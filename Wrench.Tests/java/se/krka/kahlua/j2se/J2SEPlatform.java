package se.krka.kahlua.j2se;

import se.krka.kahlua.vm.KahluaTable;
import se.krka.kahlua.vm.KahluaTableImpl;

// Test stand-in for Kahlua's J2SEPlatform.
public class J2SEPlatform {
    public KahluaTable newTable() {
        return new KahluaTableImpl();
    }
}
