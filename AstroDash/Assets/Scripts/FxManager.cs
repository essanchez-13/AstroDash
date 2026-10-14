using UnityEngine;

/// <summary>
/// Punto único para efectos visuales compartidos. Vive en la escena
/// con un pool de explosiones; cualquier script pide un efecto sin
/// preocuparse del reciclaje (mismo patrón de pooling que balas y
/// meteoritos: nada de Instantiate/Destroy en caliente).
/// </summary>
public class FxManager : MonoBehaviour
{
    public static FxManager Instance { get; private set; }

    [SerializeField] private SimplePool explosionPool;

    private void Awake() => Instance = this;

    /// Pequeño estallido de fragmentos del color del objeto destruido.
    /// Sutil a propósito: feedback de impacto sin tapar la visión —
    /// y SIN sonido: solo la muerte del jugador suena (gameOverClip).
    public void Explode(Vector3 position, Color color)
    {
        if (explosionPool == null) return;
        explosionPool.Get(position).GetComponent<ExplosionFx>().Play(color);
    }
}
