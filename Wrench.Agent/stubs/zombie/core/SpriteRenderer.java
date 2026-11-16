package zombie.core;

import java.util.function.Consumer;
import zombie.core.textures.Texture;
import zombie.core.textures.TextureDraw;

public final class SpriteRenderer {
    public static final SpriteRenderer instance = null;

    public void renderi(Texture texture, int x, int y, int width, int height, float r, float g, float b, float a,
                        Consumer<TextureDraw> consumer) {
        throw new UnsupportedOperationException("stub");
    }
}
