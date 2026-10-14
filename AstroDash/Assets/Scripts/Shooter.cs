using UnityEngine;

/// <summary>
/// Disparo automático de la nave. Equivalente al bloque `fireTimer`
/// del prototipo: acumula tiempo y dispara cada `fireInterval` segundos.
/// Va como componente del Player, con un hijo vacío "Muzzle" colocado
/// en la punta de la nave como punto de salida de las balas.
/// </summary>
public class Shooter : MonoBehaviour
{
    [SerializeField] private SimplePool bulletPool;
    [SerializeField] private Transform muzzle;   // punta de la nave

    [Header("Sonido de disparo")]
    [SerializeField] private AudioClip shotClip;
    [Tooltip("Sutil a propósito: el disparo suena hasta 7 veces por segundo")]
    [Range(0f, 1f)]
    [SerializeField] private float shotVolume = 0.3f;

    private float timer;
    private PlayerPowerUps powerUps;   // en el mismo GameObject (Player)

    private void Awake() => powerUps = GetComponent<PlayerPowerUps>();

    private void Update()
    {
        if (GameManager.Instance.State != GameState.Playing) return;
        var cfg = GameManager.Instance.Config;

        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            // Disparo rápido: el intervalo se reduce (0.5 = doble cadencia)
            timer = cfg.fireInterval *
                (powerUps.IsActive(PowerUpType.RapidFire) ? cfg.rapidRateMult : 1f);

            // Potencia: bala grande con doble daño (combinable con doble)
            bool big = powerUps.IsActive(PowerUpType.BigShot);

            // Disparo doble: dos balas en paralelo en vez de una central
            if (powerUps.IsActive(PowerUpType.DoubleShot))
            {
                float off = cfg.doubleShotOffset + (big ? 0.08f : 0f);
                Fire(muzzle.position + Vector3.left * off, big);
                Fire(muzzle.position + Vector3.right * off, big);
            }
            else
            {
                Fire(muzzle.position, big);
            }

            // UN sonido por ráfaga (con doble serían dos superpuestos =
            // el doble de fuerte); PlayOneShot permite solaparse entre
            // ráfagas seguidas sin cortarse
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySfx(shotClip, shotVolume);
        }
    }

    // Saca una bala del pool y la configura (normal o de Potencia)
    private void Fire(Vector3 position, bool big)
    {
        bulletPool.Get(position).GetComponent<Bullet>().Setup(big);
    }
}
