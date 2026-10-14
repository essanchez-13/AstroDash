using UnityEngine;

/// <summary>
/// Proyectil: sube en línea recta y se devuelve al pool al salir de
/// pantalla. NO detecta colisiones él mismo — el impacto lo gestiona
/// Meteor.OnTriggerEnter2D (basta con que uno de los dos lo haga).
///
/// Con el power-up de POTENCIA la bala sale más grande, verde y con
/// doble daño (Setup lo configura en cada disparo; como los proyectiles
/// se reciclan del pool, hay que re-configurarlos siempre).
///
/// Setup del prefab: SpriteRenderer (cápsula pequeña), tag "Bullet",
/// CapsuleCollider2D con "Is Trigger", Rigidbody2D Kinematic.
/// </summary>
public class Bullet : MonoBehaviour
{
    public int Damage { get; private set; } = 1;

    private Vector3 baseScale;
    private SpriteRenderer sr;

    private void Awake()
    {
        baseScale = transform.localScale;   // escala del prefab (bala normal)
        sr = GetComponent<SpriteRenderer>();
    }

    /// La llama Shooter en cada disparo
    public void Setup(bool big)
    {
        var cfg = GameManager.Instance.Config;
        Damage = big ? cfg.bigBulletDamage : 1;
        transform.localScale = big ? baseScale * cfg.bigBulletScale : baseScale;

        // Color del TIPO DE DISPARO equipado (tienda). La bala de
        // Potencia usa EXACTAMENTE el mismo color — el tamaño ya la
        // distingue de sobra
        sr.color = Catalog.Instance != null
            ? Catalog.Instance.GetShot(Progress.EquippedShot).color
            : Color.white;
    }

    private void Update()
    {
        transform.Translate(Vector3.up * GameManager.Instance.Config.bulletSpeed * Time.deltaTime);

        // Salió por arriba → reciclar (equivale al filter() del prototipo)
        if (transform.position.y > ScreenBounds.HalfHeight + 1f)
            GetComponent<PooledObject>().Release();
    }
}
