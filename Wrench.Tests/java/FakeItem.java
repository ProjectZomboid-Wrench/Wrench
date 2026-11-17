public class FakeItem {
    private final String fullType;
    private String name;

    public FakeItem(String fullType, String name) {
        this.fullType = fullType;
        this.name = name;
    }

    public String getFullType() {
        return fullType;
    }

    public String getName() {
        return name;
    }

    public void setName(String name) {
        this.name = name;
    }

    @Override
    public String toString() {
        return name;
    }
}
