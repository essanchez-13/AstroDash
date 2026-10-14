using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Localización mínima ES/EN. Una tabla estática de claves → (es, en);
/// el idioma vive en PlayerPrefs y al cambiarlo se avisa por evento a
/// todos los textos (LocalizedText) y controladores suscritos.
///
/// Primera vez: se elige según el idioma del sistema del teléfono.
/// Los NOMBRES PROPIOS (naves, disparos, "ASTRO DASH") no se traducen.
/// </summary>
public static class Loc
{
    private const string PrefKey = "astrodash_lang";
    private static string lang;   // "es" | "en"

    public static event Action OnChanged;

    public static string Lang
    {
        get
        {
            if (lang == null)
                lang = PlayerPrefs.GetString(PrefKey,
                    Application.systemLanguage == SystemLanguage.Spanish ? "es" : "en");
            return lang;
        }
        set
        {
            if (value != "es" && value != "en") return;
            lang = value;
            PlayerPrefs.SetString(PrefKey, value);
            PlayerPrefs.Save();
            OnChanged?.Invoke();
        }
    }

    /// Traducción de una clave al idioma activo (la propia clave si falta,
    /// para que un texto sin traducir se detecte a simple vista)
    public static string T(string key)
        => Table.TryGetValue(key, out var t) ? (Lang == "es" ? t.es : t.en) : key;

    private static readonly Dictionary<string, (string es, string en)> Table = new()
    {
        // Menú principal
        ["play"] = ("JUGAR", "PLAY"),
        ["shop"] = ("TIENDA", "SHOP"),
        ["settings"] = ("CONFIGURACIÓN", "SETTINGS"),
        ["hint"] = ("Arrastra para moverte · Disparo automático\nEsquiva las rocas y atrapa las estrellas",
                    "Drag to move · Auto-fire\nDodge the rocks and catch the stars"),
        ["best"] = ("Récord", "Best"),

        // Configuración
        ["master_volume"] = ("VOLUMEN GENERAL", "MASTER VOLUME"),
        ["music"] = ("MÚSICA", "MUSIC"),
        ["sfx"] = ("EFECTOS", "SOUND FX"),
        ["language"] = ("IDIOMA", "LANGUAGE"),
        ["back"] = ("VOLVER", "BACK"),

        // Tienda
        ["ships"] = ("NAVES", "SHIPS"),
        ["shots"] = ("DISPAROS", "SHOTS"),
        ["backgrounds"] = ("FONDOS", "THEMES"),
        ["equip"] = ("EQUIPAR", "EQUIP"),
        ["equipped"] = ("EQUIPADO", "EQUIPPED"),

        // Pausa
        ["paused"] = ("PAUSA", "PAUSED"),
        ["resume"] = ("REANUDAR", "RESUME"),
        ["quit_menu"] = ("SALIR AL MENÚ", "QUIT TO MENU"),

        // Game over
        ["game_over"] = ("FIN DEL VIAJE", "JOURNEY'S END"),
        ["tap_retry"] = ("TOCA PARA REINTENTAR", "TAP TO RETRY"),
        ["menu"] = ("MENÚ", "MENU"),
    };
}
