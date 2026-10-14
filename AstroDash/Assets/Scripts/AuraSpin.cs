using UnityEngine;

/// <summary>
/// Giro decorativo lento de las auras de power-up. Las dos auras giran
/// en sentidos opuestos, así cuando escudo e imán están activos a la
/// vez se distinguen ambas capas superpuestas.
/// </summary>
public class AuraSpin : MonoBehaviour
{
    public float degreesPerSecond = 25f;

    private void Update() =>
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
}
