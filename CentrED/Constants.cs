namespace CentrED;

public static class Constants
{
    // Reciprocal square root of two. The renderer uses it to convert between
    // square tile dimensions and their projected diagonal width in isometric
    // space without recalculating the value at runtime.
    public const float RSQRT2 = 0.70710678118654752440084436210485f;

    // Width of a tile after projection onto the screen plane. The original 44px
    // tile footprint is scaled by RSQRT2 to match the editor's coordinate math.
    public const float TILE_SIZE = 44 * RSQRT2;

    // Vertical exaggeration used when mapping tile Z values into screen-space
    // height. A single map Z step corresponds to four rendered units.
    public const float TILE_Z_SCALE = 4.0f;
}