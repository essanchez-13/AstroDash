using UnityEngine;

/// <summary>
/// Estallido one-shot reciclable: FxManager lo saca del pool, Play()
/// lo tiñe con el color del objeto destruido y lo dispara; cuando la
/// última partícula muere, vuelve solo al pool.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class ExplosionFx : MonoBehaviour
{
    private ParticleSystem ps;

    private void Awake() => ps = GetComponent<ParticleSystem>();

    // Al salir del pool: sin restos de la explosión anterior
    private void OnEnable() => ps.Clear();

    public void Play(Color color)
    {
        var main = ps.main;
        // Rango de tono (color y una versión más oscura) para que los
        // fragmentos no se vean planos
        main.startColor = new ParticleSystem.MinMaxGradient(color * 0.75f, color);
        ps.Clear();
        ps.Play();
    }

    private void Update()
    {
        if (!ps.IsAlive())
            GetComponent<PooledObject>().Release();
    }
}
