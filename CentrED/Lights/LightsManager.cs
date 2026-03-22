using ClassicUO.Renderer;
using ClassicUO.Renderer.Lights;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CentrED.Lights;

/// <summary>
/// Owns shared lighting resources and editor lighting settings.
/// </summary>
public class LightsManager
{
    private static LightsManager _instance;

    /// <summary>
    /// Gets the shared lighting manager instance.
    /// </summary>
    public static LightsManager Instance => _instance;
    
    private static readonly BlendState DarknessBlend = new()
    {
        ColorSourceBlend = Blend.Zero,
        ColorDestinationBlend = Blend.SourceColor,
        ColorBlendFunction = BlendFunction.Add,
    };

    private static readonly BlendState AltLightsBlend = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
    };
    
    private static Color DefaultApplyBlendColor = Color.White;
    private static Color AltLightsApplyBlendColor = new(0.5f, 0.5f, 0.5f);
    
    /// <summary>
    /// Gets the blend state used when applying the light pass.
    /// </summary>
    public BlendState ApplyBlendState => Instance.AltLights ? AltLightsBlend : DarknessBlend;

    /// <summary>
    /// Gets the multiply color used when applying the light pass.
    /// </summary>
    public Color ApplyBlendColor => Instance.AltLights ? AltLightsApplyBlendColor : DefaultApplyBlendColor;
    

    /// <summary>
    /// Gets or sets a value indicating whether lights use per-light colors.
    /// </summary>
    public bool ColoredLights = true;

    /// <summary>
    /// Gets or sets a value indicating whether alternate light blending is enabled.
    /// </summary>
    public bool AltLights = false;

    /// <summary>
    /// Gets or sets a value indicating whether nights render darker than the default lighting curve.
    /// </summary>
    public bool DarkNights = false;

    /// <summary>
    /// Gets or sets a value indicating whether invisible light-source sprites should be shown.
    /// </summary>
    public bool ShowInvisibleLights = false;

    /// <summary>
    /// Gets the debug-visible light id used for invisible light sources.
    /// </summary>
    public readonly ushort VisibleLightId = 0x3EE8;

    /// <summary>
    /// Gets or sets a value indicating whether ClassicUO-style terrain normals are used.
    /// </summary>
    public bool ClassicUONormals = false;

    /// <summary>
    /// Gets or sets the global light level.
    /// </summary>
    public int GlobalLightLevel = 30;

    /// <summary>
    /// Gets a value indicating whether the global light is at its maximum brightness.
    /// </summary>
    public bool MaxGlobalLight => GlobalLightLevel == 30;

    private Color _globalLightLevelColor;

    /// <summary>
    /// Gets the color applied for the current global light level.
    /// </summary>
    public Color GlobalLightLevelColor => AltLights ? Color.Black : _globalLightLevelColor;

    /// <summary>
    /// Recomputes the cached global-light tint from the current settings.
    /// </summary>
    public void UpdateGlobalLight()
    {
        var val = (GlobalLightLevel + 2) * 0.03125f;
        if (DarkNights)
        {
            val -= 0.04f;
        }
        _globalLightLevelColor = new Color(val, val, val, 1f);
    }
    
    
    const int TEXTURE_WIDTH = 32;
    const int TEXTURE_HEIGHT = 63;
    
    private Light _lights;

    /// <summary>
    /// Gets the texture that stores precomputed light colors.
    /// </summary>
    public readonly Texture2D LightColorsTexture;

    /// <summary>
    /// Creates the shared lighting manager.
    /// </summary>
    /// <param name="gd">The graphics device used to create lighting textures.</param>
    public static void Load(GraphicsDevice gd)
    {
        _instance = new LightsManager(gd);
    }
    
    /// <summary>
    /// Initializes shared lighting resources.
    /// </summary>
    /// <param name="gd">The graphics device used to create lighting textures.</param>
    private unsafe LightsManager(GraphicsDevice gd)
    {
        _lights = new Light(Application.CEDGame.MapManager.UoFileManager.Lights, gd);
        LightColorsTexture = new Texture2D(gd, TEXTURE_WIDTH, TEXTURE_HEIGHT);
        uint[] buffer = System.Buffers.ArrayPool<uint>.Shared.Rent(TEXTURE_WIDTH * TEXTURE_HEIGHT);
        fixed (uint* ptr = buffer)
        {
            LightColors.CreateLightTextures(buffer, TEXTURE_HEIGHT);
            LightColorsTexture.SetDataPointerEXT(0, null, (IntPtr)ptr, TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(uint));
        }
        LightColors.LoadLights();
        UpdateGlobalLight();
    }

    /// <summary>
    /// Gets a light sprite by identifier.
    /// </summary>
    /// <param name="id">The light identifier.</param>
    /// <returns>The matching light sprite info.</returns>
    public SpriteInfo GetLight(uint id)
    {
        return _lights.GetLight(id);
    }
}