using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PERSISTENCIA del progreso del jugador: saldo de estrellas (moneda),
/// ítems comprados y equipamiento actual (nave + disparo + fondo, cada
/// uno independiente). Se guarda como JSON dentro de PlayerPrefs — un
/// solo bloque legible y fácil de migrar a archivo o a la nube después.
///
/// Uso: Progress.Stars, Progress.TrySpend(100), Progress.Own("ship_2"),
/// Progress.EquippedShip = "ship_2" ... Todo guarda automáticamente.
/// </summary>
public static class Progress
{
    private const string Key = "astrodash_progress";

    [System.Serializable]
    private class Data
    {
        public int stars;
        public List<string> owned = new List<string>();
        public string ship = "ship_0";
        public string shot = "shot_0";
        public string background = "bg_0";
    }

    private static Data data;

    private static Data D
    {
        get
        {
            if (data == null)
            {
                string json = PlayerPrefs.GetString(Key, "");
                data = string.IsNullOrEmpty(json) ? new Data() : JsonUtility.FromJson<Data>(json);
                // Los ítems por defecto siempre son del jugador
                foreach (var id in new[] { "ship_0", "shot_0", "bg_0" })
                    if (!data.owned.Contains(id)) data.owned.Add(id);
            }
            return data;
        }
    }

    private static void Save()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(D));
        PlayerPrefs.Save();
    }

    // ----- Moneda -----
    public static int Stars => D.stars;

    public static void AddStars(int amount)
    {
        D.stars += amount;
        Save();
    }

    /// Devuelve false (sin gastar) si no alcanza el saldo
    public static bool TrySpend(int amount)
    {
        if (D.stars < amount) return false;
        D.stars -= amount;
        Save();
        return true;
    }

    // ----- Inventario -----

    /// MODO PRUEBA: lo activa GameManager desde GameConfig
    /// (unlockAllForTesting). Con esto todo cuenta como comprado, sin
    /// tocar el guardado real — al desactivarlo vuelve el progreso normal.
    public static bool UnlockAllOverride;

    public static bool IsOwned(string id) => UnlockAllOverride || D.owned.Contains(id);

    public static void Own(string id)
    {
        if (IsOwned(id)) return;
        D.owned.Add(id);
        Save();
    }

    // ----- Equipamiento (nave, disparo y fondo son independientes) -----
    public static string EquippedShip
    {
        get => D.ship;
        set { D.ship = value; Save(); }
    }

    public static string EquippedShot
    {
        get => D.shot;
        set { D.shot = value; Save(); }
    }

    public static string EquippedBackground
    {
        get => D.background;
        set { D.background = value; Save(); }
    }
}
