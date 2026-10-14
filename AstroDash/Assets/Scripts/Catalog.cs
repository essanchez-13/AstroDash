using UnityEngine;

/// <summary>
/// CATÁLOGO de ítems del juego (naves, tipos de disparo y fondos), con
/// precios en estrellas. Vive en el objeto GameManager; los arrays los
/// cablea la Fase 13 del editor. Es la única fuente de verdad: la
/// tienda lo lee para pintarse y el gameplay lo consulta al equipar.
///
/// Cada nave trae su color de bala "de diseño" (referencia visual en la
/// tienda), pero el jugador equipa nave y disparo POR SEPARADO.
/// </summary>
public class Catalog : MonoBehaviour
{
    public static Catalog Instance { get; private set; }

    [System.Serializable]
    public class ShipItem
    {
        public string id;
        public string displayName;
        public int price;
        public Sprite sprite;
        public Color designBulletColor;   // color de bala sugerido por diseño
    }

    [System.Serializable]
    public class ShotItem
    {
        public string id;
        public string displayName;
        public int price;
        public Color color;
    }

    [System.Serializable]
    public class BackgroundItem
    {
        public string id;
        public string displayName;
        public int price;
        public Color cameraColor;
    }

    public ShipItem[] ships;
    public ShotItem[] shots;
    public BackgroundItem[] backgrounds;

    private void Awake() => Instance = this;

    public ShipItem GetShip(string id)
    {
        foreach (var s in ships) if (s.id == id) return s;
        return ships[0];   // si algo falta, siempre hay nave por defecto
    }

    public ShotItem GetShot(string id)
    {
        foreach (var s in shots) if (s.id == id) return s;
        return shots[0];
    }

    public BackgroundItem GetBackground(string id)
    {
        foreach (var b in backgrounds) if (b.id == id) return b;
        return backgrounds[0];
    }
}
