using System.Numerics;
using Microsoft.Xna.Framework;
using Plane = System.Numerics.Plane;
using Rectangle = System.Drawing.Rectangle;
using Vector3 = System.Numerics.Vector3;

namespace CentrED;

/// <summary>
/// Represents the editor camera used to project map space into the isometric viewport.
/// </summary>
public class Camera
{
    /// <summary>
    /// Controls the viewport zoom factor where <c>1.0</c> is the default authored scale.
    /// </summary>
    // 1.0 is the authored scale for the editor view. Values above that zoom in
    // and values below it zoom out.
    public float Zoom = 1.0f;

    /// <summary>
    /// Defines the current viewport size used when building the projection matrix.
    /// </summary>
    public Rectangle ScreenSize;

    // Mirror the X axis so the final projection matches the map/editor screen
    // orientation expected by the rest of the rendering code.
    private Matrix4x4 _mirrorX = Matrix4x4.CreateReflection(new Plane(-1, 0, 0, 0));

    // The camera looks down at the map from an isometric angle, so the "up"
    // vector is intentionally diagonal rather than world-space +Y.
    private Vector3 _up = new(-1, -1, 0);

    // Convert world coordinates into the oblique projection used by Ultima-style
    // tile rendering, folding height into the screen-space Y component.
    private Matrix4x4 _oblique = new (1, 0, 0, 0, 
                                      0, 1, 0, 0, 
                                      0, 1, 1, 0, 
                                      0, 0, 0, 1);

    // Shift projected map content into the visible viewport instead of leaving
    // it centered around the origin.
    private Matrix4x4 _translation = Matrix4x4.CreateTranslation(new Vector3(0, 128 * 6, 0));

    /// <summary>
    /// Stores the camera position in world space.
    /// </summary>
    // Start above the map at the same height used by the projection offset.
    public Vector3 Position = new(0, 0, 128 * 6);

    /// <summary>
    /// Gets the world-space point the camera targets on the ground plane.
    /// </summary>
    // Always aim straight down at the ground plane; rotation is applied later
    // through the yaw/pitch/roll matrix.
    public Vector3 LookAt => new(Position.X, Position.Y, 0);
    
    /// <summary>
    /// Gets or sets the yaw angle, in degrees, applied to the camera view.
    /// </summary>
    public float Yaw;

    /// <summary>
    /// Gets or sets the pitch angle, in degrees, applied to the camera view.
    /// </summary>
    public float Pitch;

    /// <summary>
    /// Gets or sets the roll angle, in degrees, applied to the camera view.
    /// </summary>
    public float Roll;

    /// <summary>
    /// Stores the world transform used for camera composition.
    /// </summary>
    public Matrix4x4 world;

    /// <summary>
    /// Stores the current view transform.
    /// </summary>
    public Matrix4x4 view;

    /// <summary>
    /// Stores the current projection transform.
    /// </summary>
    public Matrix4x4 proj;

    /// <summary>
    /// Gets the combined world, view, and projection matrix in <see cref="Matrix4x4"/> form.
    /// </summary>
    // Cache the combined matrix both as System.Numerics and XNA/FNA types
    // because different rendering paths in the editor expect different structs.
    public Matrix4x4 WorldViewProj { get; private set; }

    /// <summary>
    /// Gets the combined world, view, and projection matrix converted for FNA effects.
    /// </summary>
    public Matrix FnaWorldViewProj { get; private set; }
    
    /// <summary>
    /// Restores the default zoom and rotation values for the camera.
    /// </summary>
    public void ResetCamera()
    {
        // Reset orientation and zoom without moving the camera target so the
        // user can quickly return to the default editing view.
        Zoom = 1.0f;
        Yaw = 0f;
        Pitch = 0f;
        Roll = 0f;
    }

    /// <summary>
    /// Adjusts the zoom factor by the supplied delta while keeping it within the supported range.
    /// </summary>
    /// <param name="delta">The amount to add to the current zoom factor.</param>
    public void ZoomIn(float delta)
    {
        // Clamp zoom to a practical range so the editor does not end up too far
        // in or out to render usefully.
        Zoom = Math.Clamp(Zoom + delta, 0.2f, 4f);
    }

    /// <summary>
    /// Rebuilds the camera matrices for the current position, orientation, and viewport.
    /// </summary>
    public void Update()
    {
        // Tile positions are already expressed in world space, so no additional
        // object transform is needed before building the view/projection.
        world = Matrix4x4.Identity;

        // First build the base look-at matrix, then apply user-controlled
        // rotations around the camera axes.
        view = Matrix4x4.CreateLookAt(Position, LookAt, _up);
        var ypr = Matrix4x4.CreateFromYawPitchRoll(float.DegreesToRadians(Yaw), float.DegreesToRadians(Pitch), float.DegreesToRadians(Roll));
        view = Matrix4x4.Multiply(view, ypr);

        // Orthographic projection keeps tile sizes constant regardless of depth,
        // which is what the editor wants for a map-style view.
        Matrix4x4 ortho = Matrix4x4.CreateOrthographic(ScreenSize.Width, ScreenSize.Height, 0, 128 * 12);

        // Zoom is applied in projection space so it scales the final screen view
        // without changing the camera position.
        Matrix4x4 scale = Matrix4x4.CreateScale(Zoom, Zoom, 1f);

        // Compose the projection in the order needed for the editor's 2.5D
        // presentation: mirror, skew into oblique space, translate into view,
        // then apply orthographic projection and zoom.
        proj = _mirrorX * _oblique * _translation * ortho * scale;
        
        var worldView = Matrix4x4.Multiply(world, view);
        WorldViewProj = Matrix4x4.Multiply(worldView, proj);

        // FNA effects consume Microsoft.Xna.Framework.Matrix, so mirror the
        // final transform into that type once per update.
        FnaWorldViewProj = new Matrix(
            WorldViewProj.M11, WorldViewProj.M12, WorldViewProj.M13, WorldViewProj.M14,
            WorldViewProj.M21, WorldViewProj.M22, WorldViewProj.M23, WorldViewProj.M24,
            WorldViewProj.M31, WorldViewProj.M32, WorldViewProj.M33, WorldViewProj.M34,
            WorldViewProj.M41, WorldViewProj.M42, WorldViewProj.M43, WorldViewProj.M44
            );
    }
}