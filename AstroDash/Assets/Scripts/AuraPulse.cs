using UnityEngine;

/// <summary>
/// Latido sutil del aura de power-up: la escala respira un ±4% con una
/// onda senoidal. Junto con las capas que giran (AuraSpin) hace que el
/// aura se sienta VIVA en lugar de ser un círculo estático.
/// </summary>
public class AuraPulse : MonoBehaviour
{
    [SerializeField] private float amplitude = 0.04f;
    [SerializeField] private float speed = 3.5f;

    private Vector3 baseScale;

    private void Awake() => baseScale = transform.localScale;

    // Al reactivarse (el aura se enciende/apaga por power-up) vuelve
    // siempre a su escala base, sin acumular el latido anterior
    private void OnEnable() => transform.localScale = baseScale;

    private void Update()
        => transform.localScale = baseScale * (1f + amplitude * Mathf.Sin(Time.time * speed));
}
