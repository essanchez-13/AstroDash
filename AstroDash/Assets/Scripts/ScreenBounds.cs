using UnityEngine;

/// <summary>
/// Límites visibles del mundo calculados desde la cámara ortográfica.
/// En el prototipo el mundo medía 420x740 fijo; en Unity el ancho visible
/// depende de la pantalla del dispositivo, así que se calcula en runtime.
/// Con esto el juego se adapta solo a cualquier proporción de móvil.
/// </summary>
public static class ScreenBounds
{
    private static Camera cam;

    private static Camera Cam => cam != null ? cam : (cam = Camera.main);

    /// Media altura visible (orthographicSize de la cámara)
    public static float HalfHeight => Cam.orthographicSize;

    /// Media anchura visible (depende del aspect ratio del dispositivo)
    public static float HalfWidth => Cam.orthographicSize * Cam.aspect;
}
