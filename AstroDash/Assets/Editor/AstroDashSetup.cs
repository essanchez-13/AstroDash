using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Script de editor que monta TODO el proyecto AstroDash automáticamente
/// (lo que la guía SETUP.md describe a mano): tags, sprites geométricos
/// generados por código, prefabs, GameConfig, escena completa con UI y
/// ajustes del proyecto. Se ejecuta en batch mode en dos fases:
///
///   Unity.exe -batchmode -executeMethod AstroDashSetup.Phase1   (TMP)
///   Unity.exe -batchmode -executeMethod AstroDashSetup.Phase2   (todo lo demás)
///
/// Se separa en dos fases porque la importación del paquete de recursos
/// de TextMeshPro es asíncrona: la fase 2 necesita que ya esté completa.
/// </summary>
public static class AstroDashSetup
{
    private const string SpritesDir = "Assets/Sprites";
    private const string PrefabsDir = "Assets/Prefabs";
    private const string ScenesDir = "Assets/Scenes";
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ConfigPath = "Assets/GameConfig.asset";

    // Paleta (la misma del prototipo HTML)
    private static readonly Color32 ColBg     = new Color32(7, 11, 20, 255);      // #070B14
    private static readonly Color32 ColShip   = new Color32(34, 211, 238, 255);   // #22D3EE
    private static readonly Color32 ColBullet = new Color32(226, 232, 240, 255);  // #E2E8F0
    private static readonly Color32 ColItem   = new Color32(251, 191, 36, 255);   // #FBBF24
    private static readonly Color32 ColText   = new Color32(226, 232, 240, 255);
    private static readonly Color32 ColTextDim= new Color32(100, 116, 139, 255);  // #64748B

    // ================= FASE 1: recursos de TextMeshPro =================
    // La importación de un .unitypackage es ASÍNCRONA: no se puede llamar
    // a Exit() justo después de ImportPackage o el editor muere antes de
    // terminar. Nos suscribimos a los callbacks de fin de importación y
    // salimos desde ahí (por eso esta fase se lanza sin -quit).
    public static void Phase1()
    {
        try
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
            {
                Debug.Log("ASTRODASH PHASE1 OK (ya estaba importado)");
                EditorApplication.Exit(0);
                return;
            }

            // El unitypackage de recursos esenciales vive dentro del paquete
            // ugui (Unity 6) o textmeshpro (versiones anteriores)
            string[] candidates =
            {
                "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage",
                "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage",
            };
            string package = null;
            foreach (var pkg in candidates)
                if (File.Exists(Path.GetFullPath(pkg))) { package = pkg; break; }
            if (package == null)
                throw new FileNotFoundException("No se encontró TMP Essential Resources.unitypackage");

            AssetDatabase.importPackageCompleted += name =>
            {
                AssetDatabase.SaveAssets();
                Debug.Log("ASTRODASH PHASE1 OK");
                EditorApplication.Exit(0);
            };
            AssetDatabase.importPackageFailed += (name, error) =>
            {
                Debug.LogError("ASTRODASH PHASE1 FAILED: " + error);
                EditorApplication.Exit(1);
            };

            Debug.Log("Importando TMP essentials desde: " + package);
            AssetDatabase.ImportPackage(package, false);
        }
        catch (System.Exception e)
        {
            Debug.LogError("ASTRODASH PHASE1 FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // ================= FASE 2: proyecto completo =======================
    public static void Phase2()
    {
        try
        {
            EnsureTags("Bullet", "Meteor", "Item");
            EnsureFolders();

            // --- Sprites geométricos generados por código (sin arte externo) ---
            Sprite triangle = MakeSprite("triangle", (u, v) => PointInTri(u, v, 0f, 0.48f, -0.42f, -0.44f, 0.42f, -0.44f));
            Sprite circle   = MakeSprite("circle",   (u, v) => u * u + v * v <= 0.48f * 0.48f);
            Sprite hexagon  = MakeSprite("hexagon",  (u, v) => InRegularPolygon(u, v, 6, 0.48f));
            Sprite diamond  = MakeSprite("diamond",  (u, v) => Mathf.Abs(u) + Mathf.Abs(v) <= 0.48f);
            Sprite capsule  = MakeSprite("capsule",  InCapsule);

            // --- Configuración del juego ---
            var config = ScriptableObject.CreateInstance<GameConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);

            // --- Prefabs ---
            GameObject bulletPrefab = MakeBulletPrefab(capsule);
            GameObject meteorPrefab = MakeMeteorPrefab(hexagon);
            GameObject itemPrefab   = MakeItemPrefab(diamond);
            GameObject starPrefab   = MakeStarPrefab(circle);

            // --- Escena ---
            BuildScene(config, bulletPrefab, meteorPrefab, itemPrefab, starPrefab, triangle);

            // --- Ajustes del proyecto ---
            PlayerSettings.companyName = "AstroDash";
            PlayerSettings.productName = "Astro Dash";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ASTRODASH PHASE2 OK");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("ASTRODASH PHASE2 FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // ================= FASE 3: power-ups ===============================
    // Añade al proyecto ya montado todo lo necesario para los power-ups:
    // tag, sprites nuevos, prefab PowerUp (con su letra en TextMeshPro),
    // pool en escena, PlayerPowerUps + aura de escudo en el Player y la
    // línea de estado en el HUD. Es idempotente: se puede re-ejecutar.
    public static void Phase3()   // entrada para batch: cierra el proceso al acabar
    {
        try { Phase3Work(); Debug.Log("ASTRODASH PHASE3 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE3 FAILED: " + e); EditorApplication.Exit(1); }
    }

    /// La misma instalación pero desde el menú del editor, SIN cerrar
    /// Unity — para cuando el proyecto está abierto y el batch no puede
    /// entrar. Es idempotente: repetirla no duplica nada.
    [MenuItem("AstroDash/Fase 3 - Instalar power-ups")]
    public static void Phase3FromMenu()
    {
        Phase3Work();
        Debug.Log("ASTRODASH PHASE3 OK");
    }

    private static void Phase3Work()
    {
        {
            EnsureTags("PowerUp");
            EnsureFolders();

            // --- Sprites nuevos ---
            Sprite square = MakeSprite("roundedsquare", InRoundedSquare);
            Sprite ring = MakeSprite("ring", (u, v) =>
            {
                float d = Mathf.Sqrt(u * u + v * v);      // anillo: corona circular
                return d >= 0.40f && d <= 0.48f;
            });

            // --- Prefab PowerUp ---
            GameObject powerUpPrefab = MakePowerUpPrefab(square);

            // --- Modificaciones sobre la escena existente ---
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            // Pool de power-ups cableado al Spawner
            var spawner = Object.FindFirstObjectByType<Spawner>();
            var poolGo = GameObject.Find("PowerUpPool");
            SimplePool pool = poolGo != null
                ? poolGo.GetComponent<SimplePool>()
                : MakePool("PowerUpPool", powerUpPrefab, 4);
            SetField(spawner, "powerUpPool", pool);

            // Player: componente de temporizadores + aura del escudo
            var player = Object.FindFirstObjectByType<PlayerController>().gameObject;
            var powerUps = player.GetComponent<PlayerPowerUps>();
            if (powerUps == null) powerUps = player.AddComponent<PlayerPowerUps>();

            var ringTf = player.transform.Find("ShieldRing");
            GameObject ringGo;
            if (ringTf == null)
            {
                ringGo = new GameObject("ShieldRing");
                ringGo.transform.SetParent(player.transform, false);
                // Compensa la escala no uniforme del Player (0.6 x 0.7)
                // para que el aura sea un círculo de ~1.5 unidades
                ringGo.transform.localScale = new Vector3(2.5f, 2.15f, 1f);
                var rsr = ringGo.AddComponent<SpriteRenderer>();
                rsr.sprite = ring;
                rsr.color = new Color(34f / 255f, 211f / 255f, 238f / 255f, 0.55f);
                rsr.sortingOrder = 1;
                ringGo.SetActive(false);   // solo visible con escudo activo
            }
            else ringGo = ringTf.gameObject;
            SetField(powerUps, "shieldVisual", ringGo);

            // HUD: línea de power-ups activos bajo el marcador
            var hud = Object.FindFirstObjectByType<HUDController>();
            var txtTf = hud.transform.Find("PowerUpText");
            TextMeshProUGUI powerText = txtTf != null
                ? txtTf.GetComponent<TextMeshProUGUI>()
                : MakeText(hud.transform, "PowerUpText", "", 44, ColText, true,
                    new Vector2(0.5f, 1f), new Vector2(0f, -350f), new Vector2(900f, 70f));
            powerText.richText = true;   // colores por tipo vía rich text
            SetField(hud, "powerUpText", powerText);

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }

    // Cuadrado con esquinas redondeadas (cápsula de power-up)
    private static bool InRoundedSquare(float u, float v)
    {
        const float half = 0.42f, corner = 0.14f;
        float ax = Mathf.Abs(u), ay = Mathf.Abs(v);
        if (ax > half || ay > half) return false;
        float cx = half - corner;
        if (ax <= cx || ay <= cx) return true;          // zona recta
        float dx = ax - cx, dy = ay - cx;                // esquina circular
        return dx * dx + dy * dy <= corner * corner;
    }

    private static GameObject MakePowerUpPrefab(Sprite square)
    {
        var go = new GameObject("PowerUp") { tag = "PowerUp" };
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = square;                 // el color lo pone Setup() por tipo
        go.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        AddKinematicBody(go);
        var item = go.AddComponent<PowerUpItem>();

        // Letra central: TextMeshPro de MUNDO (no UGUI), hijo del prefab
        var letterGo = new GameObject("Letter");
        letterGo.transform.SetParent(go.transform, false);
        var tmp = letterGo.AddComponent<TextMeshPro>();
        tmp.text = "?";                     // la letra real la pone Setup()
        tmp.fontSize = 5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = new Color(0.03f, 0.04f, 0.08f);   // oscuro sobre el color
        ((RectTransform)letterGo.transform).sizeDelta = new Vector2(2f, 2f);
        letterGo.GetComponent<MeshRenderer>().sortingOrder = 1;

        // (la letra ya no se cablea: desde la Fase 6 el prefab usa los
        //  iconos de powerups.png y la Fase 6 elimina este hijo TMP)
        return SavePrefab(go, "PowerUp");
    }

    // ================= FASE 4: arte mejorado ===========================
    // Regenera los sprites con sombreado, gradientes y brillo dibujados
    // por código (mismos archivos PNG → mismos GUID → los prefabs y la
    // escena se actualizan solos) y pone en blanco los tintes de los
    // renderers cuyo color ahora va "horneado" en la textura.
    public static void Phase4()
    {
        try { Phase4Work(); Debug.Log("ASTRODASH PHASE4 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE4 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 4 - Arte mejorado")]
    public static void Phase4FromMenu()
    {
        Phase4Work();
        Debug.Log("ASTRODASH PHASE4 OK");
    }

    private static void Phase4Work()
    {
        // --- Texturas nuevas sobre los mismos archivos ---
        // (nave y meteoritos ya NO se generan por código: desde la Fase 5
        //  usan los sprites importados nave.png y asteroides.png)
        MakeSpriteRGBA("diamond", ShadeStar);          // estrella dorada con halo
        MakeSpriteRGBA("capsule", ShadeBullet);        // bala con núcleo y brillo
        MakeSpriteRGBA("circle", ShadeGlowDot);        // punto suave (fondo)

        // --- Renderers que teñían: el color ahora va en la textura ---
        SetPrefabTint("Item", Color.white);
        SetPrefabTint("Bullet", Color.white);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<PlayerController>();
        player.GetComponent<SpriteRenderer>().color = Color.white;
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void SetPrefabTint(string name, Color color)
    {
        string path = $"{PrefabsDir}/{name}.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        root.GetComponent<SpriteRenderer>().color = color;
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Igual que MakeSprite pero con COLOR por píxel (supermuestreo 2x2)
    private static Sprite MakeSpriteRGBA(string name, System.Func<float, float, Color> shade)
    {
        const int SIZE = 256;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var pixels = new Color[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            Color acc = Color.clear;
            for (int sy = 0; sy < 2; sy++)
            for (int sx = 0; sx < 2; sx++)
            {
                float u = (x + 0.25f + sx * 0.5f) / SIZE - 0.5f;
                float v = (y + 0.25f + sy * 0.5f) / SIZE - 0.5f;
                acc += shade(u, v);
            }
            pixels[y * SIZE + x] = acc / 4f;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        string path = $"{SpritesDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = SIZE;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        // SIN compresión: DXT tritura los bordes suaves (esquinas
        // redondeadas dentadas, halos sucios)
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ---- Sombreadores de píxel (u,v en [-0.5, 0.5], +v hacia arriba) ----

    // (ShadeShip, ShadeRock y Crater eliminados: la nave y los meteoritos
    //  usan sprites importados desde la Fase 5)

    private static Color ShadeStar(float u, float v)
    {
        float au = Mathf.Abs(u), av = Mathf.Abs(v);
        float d = Mathf.Sqrt(u * u + v * v);
        // Estrella de 4 puntas (norma con exponente < 1 = lados cóncavos)
        const float p = 0.55f, R = 0.42f;
        if (Mathf.Pow(au, p) + Mathf.Pow(av, p) <= Mathf.Pow(R, p))
            return Color.Lerp(new Color(1f, 0.98f, 0.84f), new Color(0.96f, 0.62f, 0.04f),
                              Mathf.Clamp01(d / R));
        float glow = Mathf.Clamp01(1f - d / 0.5f);
        return new Color(0.98f, 0.75f, 0.14f, glow * glow * 0.5f);
    }

    private static Color ShadeBullet(float u, float v)
    {
        float dy = Mathf.Max(0f, Mathf.Abs(v) - 0.24f);
        float d = Mathf.Sqrt(u * u + dy * dy);   // distancia a la cápsula central
        if (d < 0.10f) return Color.white;
        float glow = Mathf.Clamp01(1f - (d - 0.10f) / 0.30f);
        return new Color(0.75f, 0.96f, 1f, glow * glow * 0.85f);
    }

    // (ShadeCapsule eliminado: las cápsulas usan los iconos importados
    //  de powerups.png desde la Fase 6)

    private static Color ShadeGlowDot(float u, float v)
    {
        float d = Mathf.Sqrt(u * u + v * v) / 0.5f;
        float a = Mathf.Clamp01(1f - d);
        return new Color(1f, 1f, 1f, a * a);
    }

    // ================= FASE 5: sprites importados ======================
    // Importa nave.png (Single) y asteroides.png (Multiple, cortado en
    // 17 sprites con los rects calculados por el script Python que quitó
    // los fondos), cablea los arrays del prefab Meteor por clase de
    // asteroide y pone la nave nueva al jugador con su collider ajustado.

    [System.Serializable]
    private class SliceData { public int texW, texH, ppuAsteroides, ppuNave; public Slice[] slices; }
    [System.Serializable]
    private class Slice { public string name; public int x, y, w, h; }

    public static void Phase5()
    {
        try { Phase5Work(); Debug.Log("ASTRODASH PHASE5 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE5 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 5 - Sprites importados")]
    public static void Phase5FromMenu()
    {
        Phase5Work();
        Debug.Log("ASTRODASH PHASE5 OK");
    }

    private static void Phase5Work()
    {
        var data = JsonUtility.FromJson<SliceData>(
            File.ReadAllText($"{SpritesDir}/asteroides_slices.json"));

        // --- Hoja de asteroides: modo Multiple + corte en 17 sprites ---
        string astPath = $"{SpritesDir}/asteroides.png";
        AssetDatabase.ImportAsset(astPath);
        var astImp = (TextureImporter)AssetImporter.GetAtPath(astPath);
        astImp.textureType = TextureImporterType.Sprite;
        astImp.spriteImportMode = SpriteImportMode.Multiple;
        astImp.spritePixelsPerUnit = data.ppuAsteroides;   // grande ≈ 1 unidad
        astImp.alphaIsTransparency = true;
        astImp.mipmapEnabled = false;
        astImp.maxTextureSize = 2048;

        SliceSheet(astImp, data.slices);   // corte con la API del Sprite Editor

        // --- Nave: sprite único ---
        string navePath = $"{SpritesDir}/nave.png";
        AssetDatabase.ImportAsset(navePath);
        var naveImp = (TextureImporter)AssetImporter.GetAtPath(navePath);
        naveImp.textureType = TextureImporterType.Sprite;
        naveImp.spriteImportMode = SpriteImportMode.Single;
        naveImp.spritePixelsPerUnit = data.ppuNave;   // nave ≈ 0.62 unidades de ancho
        naveImp.alphaIsTransparency = true;
        naveImp.mipmapEnabled = false;
        naveImp.maxTextureSize = 2048;
        naveImp.SaveAndReimport();

        // --- Clasificar los sprites cortados por su nombre ---
        var red = new List<Sprite>();
        var grey = new List<Sprite>();
        var small = new List<Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(astPath))
        {
            if (obj is not Sprite sp) continue;
            if (sp.name.StartsWith("rojo")) red.Add(sp);
            else if (sp.name.StartsWith("gris")) grey.Add(sp);
            else small.Add(sp);
        }
        red.Sort((a, b) => a.name.CompareTo(b.name));
        grey.Sort((a, b) => a.name.CompareTo(b.name));
        small.Sort((a, b) => a.name.CompareTo(b.name));
        Debug.Log($"Sprites: {red.Count} rojos, {grey.Count} grises, {small.Count} pequeños");

        // --- Prefab Meteor: arrays por clase + sprite por defecto ---
        string meteorPath = $"{PrefabsDir}/Meteor.prefab";
        var meteor = PrefabUtility.LoadPrefabContents(meteorPath);
        var msr = meteor.GetComponent<SpriteRenderer>();
        msr.sprite = grey[0];
        msr.color = Color.white;
        var meteorSo = new SerializedObject(meteor.GetComponent<Meteor>());
        FillSpriteArray(meteorSo, "bigRedSprites", red);
        FillSpriteArray(meteorSo, "bigGreySprites", grey);
        FillSpriteArray(meteorSo, "smallSprites", small);
        meteorSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(meteor, meteorPath);
        PrefabUtility.UnloadPrefabContents(meteor);

        // --- Config: el portador pasa a medir en unidades de mundo ---
        var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
        config.carrierScale = 1.45f;
        EditorUtility.SetDirty(config);

        // --- Escena: la nave nueva del jugador ---
        var naveSprite = AssetDatabase.LoadAssetAtPath<Sprite>(navePath);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<PlayerController>();
        var psr = player.GetComponent<SpriteRenderer>();
        psr.sprite = naveSprite;
        psr.color = Color.white;
        player.transform.localScale = Vector3.one;   // el PPU ya fija el tamaño

        // Collider acorde al fuselaje (no a las alas: hitbox justa)
        var pcol = player.GetComponent<CircleCollider2D>();
        pcol.radius = 0.22f;
        pcol.offset = Vector2.zero;

        // Cañón en la punta de la nave nueva
        var muzzle = player.transform.Find("Muzzle");
        muzzle.localPosition = new Vector3(0f, naveSprite.bounds.extents.y + 0.12f, 0f);

        // Aura del escudo proporcionada a la nave nueva
        var pp = player.GetComponent<PlayerPowerUps>();
        var auraProp = new SerializedObject(pp).FindProperty("shieldVisual");
        if (auraProp != null && auraProp.objectReferenceValue is GameObject aura)
            aura.transform.localScale = Vector3.one * 1.25f;

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void FillSpriteArray(SerializedObject so, string field, List<Sprite> sprites)
    {
        if (sprites.Count == 0) throw new System.Exception($"Sin sprites para {field}");
        var prop = so.FindProperty(field);
        prop.arraySize = sprites.Count;
        for (int i = 0; i < sprites.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
    }

    /// Corta una hoja de sprites con la API moderna del Sprite Editor
    private static void SliceSheet(TextureImporter importer, Slice[] slices)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();

        var rects = new SpriteRect[slices.Length];
        for (int i = 0; i < slices.Length; i++)
        {
            var s = slices[i];
            rects[i] = new SpriteRect
            {
                name = s.name,
                rect = new Rect(s.x, s.y, s.w, s.h),   // origen abajo-izquierda
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = GUID.Generate(),
            };
        }
        provider.SetSpriteRects(rects);

        // Desde Unity 2021.2 hay que registrar también los pares nombre↔id
        var nameFileId = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        if (nameFileId != null)
        {
            var pairs = new List<SpriteNameFileIdPair>();
            foreach (var r in rects) pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
            nameFileId.SetNameFileIdPairs(pairs);
        }
        provider.Apply();
        importer.SaveAndReimport();
    }

    // ================= FASE 6: iconos de power-ups =====================
    // Re-corta la hoja de asteroides actualizada (reutiliza la Fase 5,
    // que también re-cablea los arrays del Meteor) e integra la hoja
    // powerups.png: 5 iconos circulares, uno por tipo, asignados al
    // prefab PowerUp. La letra TMP se elimina: el icono ya identifica.

    [System.Serializable]
    private class PuSliceData { public int texW, texH, ppu; public Slice[] slices; }

    public static void Phase6()
    {
        try { Phase6Work(); Debug.Log("ASTRODASH PHASE6 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE6 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 6 - Iconos de power-ups")]
    public static void Phase6FromMenu()
    {
        Phase6Work();
        Debug.Log("ASTRODASH PHASE6 OK");
    }

    private static void Phase6Work()
    {
        // 1) Asteroides actualizados: re-importar, re-cortar y re-cablear.
        //    (Los IDs de los sub-sprites cambian al re-cortar, así que
        //    volver a pasar la Fase 5 completa garantiza referencias vivas.)
        Phase5Work();

        // 2) Iconos de power-ups
        WirePowerUpIcons();

        // 3) Limpieza: la cápsula procedural ya no se usa
        AssetDatabase.DeleteAsset($"{SpritesDir}/roundedsquare.png");
        AssetDatabase.SaveAssets();
    }

    /// Importa/re-importa powerups.png, la corta con su JSON y asigna
    /// los 5 iconos al prefab PowerUp (reutilizado por las fases 6 y 8:
    /// al re-cortar cambian los IDs y hay que re-cablear siempre).
    private static void WirePowerUpIcons()
    {
        var data = JsonUtility.FromJson<PuSliceData>(
            File.ReadAllText($"{SpritesDir}/powerups_slices.json"));
        string path = $"{SpritesDir}/powerups.png";
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = data.ppu;   // icono ≈ 0.55 unidades
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        SliceSheet(imp, data.slices);

        // 3) Cargar los 5 iconos en el orden del enum PowerUpType
        string[] order = { "pu_shield", "pu_magnet", "pu_double", "pu_rapid", "pu_big" };
        var sprites = new Sprite[order.Length];
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (obj is not Sprite sp) continue;
            int idx = System.Array.IndexOf(order, sp.name);
            if (idx >= 0) sprites[idx] = sp;
        }
        for (int i = 0; i < sprites.Length; i++)
            if (sprites[i] == null)
                throw new System.Exception($"Falta el icono {order[i]} en powerups.png");

        // 4) Prefab PowerUp: iconos cableados y sin la letra TMP
        string prefabPath = $"{PrefabsDir}/PowerUp.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var sr = root.GetComponent<SpriteRenderer>();
        sr.sprite = sprites[0];
        sr.color = Color.white;

        var so = new SerializedObject(root.GetComponent<PowerUpItem>());
        var arr = so.FindProperty("typeSprites");
        arr.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        var letter = root.GetComponentInChildren<TextMeshPro>();
        if (letter != null) Object.DestroyImmediate(letter.gameObject);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // ================= FASE 8: rocas rotas, iconos 2 y audio ===========
    // 1) Iconos de power-ups actualizados (misma estructura de 5).
    // 2) Sistema de daño visual: cada rojo grande tiene su versión ROTA
    //    (rotos.png) que se muestra al quedarle un solo impacto.
    // 3) AudioManager en escena, listo para arrastrar la pista de música.

    public static void Phase8()
    {
        try { Phase8Work(); Debug.Log("ASTRODASH PHASE8 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE8 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 8 - Rocas rotas y audio")]
    public static void Phase8FromMenu()
    {
        Phase8Work();
        Debug.Log("ASTRODASH PHASE8 OK");
    }

    private static void Phase8Work()
    {
        // --- Iconos nuevos (mismos nombres/orden: re-cortar y re-cablear) ---
        WirePowerUpIcons();

        // --- Rocas rotas: importar, cortar y cablear ---
        WireBrokenRocks();

        // --- Escena: AudioManager con sus dos fuentes (música y SFX) ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var oldAudio = GameObject.Find("AudioManager");
        if (oldAudio != null) Object.DestroyImmediate(oldAudio);

        var audioGo = new GameObject("AudioManager");
        var musicSource = audioGo.AddComponent<AudioSource>();
        musicSource.loop = true;            // música de fondo en bucle
        musicSource.playOnAwake = false;    // la arranca AudioManager al haber clip
        var sfxSource = audioGo.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;

        var audio = audioGo.AddComponent<AudioManager>();
        SetField(audio, "musicSource", musicSource);
        SetField(audio, "sfxSource", sfxSource);
        // El campo "Music Clip" queda vacío a propósito: ahí arrastras tu pista

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 7: VFX y partículas ========================
    // Fuego del motor (crece con el turbo del escudo), fragmentos al
    // destruir meteoritos (sutiles, del color de la roca), auras
    // procedurales de escudo (cian) e imán (violeta) superponibles,
    // y HUD de power-ups solo con números de color.

    public static void Phase7()
    {
        try { Phase7Work(); Debug.Log("ASTRODASH PHASE7 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE7 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 7 - VFX y partículas")]
    public static void Phase7FromMenu()
    {
        Phase7Work();
        Debug.Log("ASTRODASH PHASE7 OK");
    }

    private static void Phase7Work()
    {
        // --- Sprite de aura: anillo difuminado procedural ---
        var auraSprite = MakeSpriteRGBA("aura", ShadeAura);

        // --- Material compartido de partículas (se tiñe por vértice) ---
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        const string matPath = "Assets/Materials/ParticleGlow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{SpritesDir}/circle.png");

        // --- Prefab de explosión ---
        var explosionPrefab = MakeExplosionPrefab(mat);

        // --- Escena ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<PlayerController>();
        var naveSprite = player.GetComponent<SpriteRenderer>().sprite;

        // Auras nuevas (idempotente: elimina las anteriores si existen)
        var pp = player.GetComponent<PlayerPowerUps>();
        var ppSo = new SerializedObject(pp);
        if (ppSo.FindProperty("shieldVisual").objectReferenceValue is GameObject oldAura)
            Object.DestroyImmediate(oldAura);
        DestroyChild(player.transform, "AuraShield");
        DestroyChild(player.transform, "AuraMagnet");
        var auraShield = MakeAura(player.transform, "AuraShield", auraSprite,
            new Color(34 / 255f, 211 / 255f, 238 / 255f, 0.55f), 1.35f, 25f);
        var auraMagnet = MakeAura(player.transform, "AuraMagnet", auraSprite,
            new Color(167 / 255f, 139 / 255f, 250 / 255f, 0.55f), 1.05f, -40f);
        ppSo.Update();   // refrescar tras los DestroyImmediate
        ppSo.FindProperty("shieldVisual").objectReferenceValue = auraShield;
        ppSo.FindProperty("magnetVisual").objectReferenceValue = auraMagnet;
        ppSo.ApplyModifiedPropertiesWithoutUndo();

        // Fuego del motor
        DestroyChild(player.transform, "EngineFire");
        MakeEngineFire(player.transform, naveSprite, mat);

        // FxManager con su pool de explosiones
        var oldFx = GameObject.Find("FxManager");
        if (oldFx != null) Object.DestroyImmediate(oldFx);
        var fxGo = new GameObject("FxManager");
        var fx = fxGo.AddComponent<FxManager>();
        var pool = MakePool("ExplosionPool", explosionPrefab, 8);
        pool.transform.SetParent(fxGo.transform);
        SetField(fx, "explosionPool", pool);

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void DestroyChild(Transform parent, string name)
    {
        var child = parent.Find(name);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    private static GameObject MakeAura(Transform parent, string name, Sprite sprite,
        Color tint, float scale, float spinDegPerSec)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = tint;
        sr.sortingOrder = -1;                 // detrás de la nave
        go.AddComponent<AuraSpin>().degreesPerSecond = spinDegPerSec;
        go.SetActive(false);                  // las enciende PlayerPowerUps
        return go;
    }

    private static void MakeEngineFire(Transform player, Sprite naveSprite, Material mat)
    {
        var go = new GameObject("EngineFire");
        go.transform.SetParent(player, false);
        go.transform.localPosition = new Vector3(0f, -naveSprite.bounds.extents.y - 0.02f, 0f);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // emitir hacia -Y

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.30f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.99f, 0.88f, 0.28f),   // amarillo
            new Color(0.98f, 0.57f, 0.24f));  // naranja
        main.simulationSpace = ParticleSystemSimulationSpace.World;  // deja estela al moverse
        main.maxParticles = 256;

        var emission = ps.emission;
        emission.rateOverTime = 45f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.04f;

        AddAlphaFade(ps);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.sortingOrder = -2;           // detrás de la nave y las auras

        go.AddComponent<EngineFireController>();
    }

    private static GameObject MakeExplosionPrefab(Material mat)
    {
        var go = new GameObject("MeteorExplosion");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;             // lo dispara ExplosionFx.Play()
        main.loop = false;                    // ¡UN solo estallido! (con loop,
                                              // el burst se repetía cada 5 s en
                                              // la posición vieja: "fantasmas")
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);   // fragmentos pequeños: SUTIL
        main.maxParticles = 32;
        main.stopAction = ParticleSystemStopAction.None;   // el pool lo recicla

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 9) });   // un solo estallido

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.05f;

        AddAlphaFade(ps);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.sortingOrder = 5;            // por encima de las rocas

        go.AddComponent<ExplosionFx>();
        return SavePrefab(go, "MeteorExplosion");
    }

    // Fundido de alpha 1→0 a lo largo de la vida de la partícula
    private static void AddAlphaFade(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static Color ShadeAura(float u, float v)
    {
        float d = Mathf.Sqrt(u * u + v * v);
        if (d > 0.5f) return Color.clear;
        // Anillo difuminado (gaussiana alrededor de r=0.36) con un patrón
        // angular sutil para que el giro de AuraSpin se aprecie
        float ring = Mathf.Exp(-Mathf.Pow((d - 0.36f) / 0.07f, 2f));
        float pattern = 1f + 0.25f * Mathf.Sin(Mathf.Atan2(v, u) * 6f);
        float inner = d < 0.36f ? 0.12f : 0f;   // relleno interior tenue
        float a = Mathf.Clamp01(ring * 0.85f * pattern + inner);
        return new Color(1f, 1f, 1f, a);
    }

    /// Importa/re-importa rotos.png, la corta con su JSON y asigna las
    /// 4 versiones rotas al prefab Meteor (reutilizado por las fases 8
    /// y 11: al re-cortar cambian los IDs y hay que re-cablear siempre).
    private static void WireBrokenRocks()
    {
        var data = JsonUtility.FromJson<PuSliceData>(
            File.ReadAllText($"{SpritesDir}/rotos_slices.json"));
        string path = $"{SpritesDir}/rotos.png";
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = data.ppu;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        SliceSheet(imp, data.slices);

        // roto_0..roto_3 en orden (mismo orden que los rojos originales)
        var broken = new List<Sprite>();
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
            if (obj is Sprite sp) broken.Add(sp);
        broken.Sort((a, b) => a.name.CompareTo(b.name));
        if (broken.Count != 4)
            throw new System.Exception($"Se esperaban 4 rocas rotas, hay {broken.Count}");

        string meteorPath = $"{PrefabsDir}/Meteor.prefab";
        var meteor = PrefabUtility.LoadPrefabContents(meteorPath);
        var meteorSo = new SerializedObject(meteor.GetComponent<Meteor>());
        FillSpriteArray(meteorSo, "bigRedBrokenSprites", broken);
        meteorSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(meteor, meteorPath);
        PrefabUtility.UnloadPrefabContents(meteor);
    }

    // ================= FASE 9: música de fondo =========================
    // Importa el paquete "Arcade Game BGM 3" descargado del Asset Store
    // (está en el caché del usuario, no en el proyecto) y asigna la
    // pista al AudioManager. La importación de un .unitypackage es
    // ASÍNCRONA: la asignación ocurre en el callback, igual que en la
    // Fase 1 (por eso esta fase se lanza sin -quit).
    private static readonly string MusicPackagePath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        @"Unity\Asset Store-5.x\BGM\AudioMusic\Arcade Game BGM 3.unitypackage");

    public static void Phase9()
    {
        try
        {
            // Si los clips ya están importados, solo asignar
            if (FindMusicClips().Count > 0)
            {
                AssignMusic();
                Debug.Log("ASTRODASH PHASE9 OK");
                EditorApplication.Exit(0);
                return;
            }

            if (!File.Exists(MusicPackagePath))
                throw new FileNotFoundException("No está el paquete: " + MusicPackagePath);

            AssetDatabase.importPackageCompleted += name =>
            {
                try
                {
                    AssignMusic();
                    Debug.Log("ASTRODASH PHASE9 OK");
                    EditorApplication.Exit(0);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("ASTRODASH PHASE9 FAILED: " + e);
                    EditorApplication.Exit(1);
                }
            };
            AssetDatabase.importPackageFailed += (name, error) =>
            {
                Debug.LogError("ASTRODASH PHASE9 FAILED: " + error);
                EditorApplication.Exit(1);
            };
            AssetDatabase.ImportPackage(MusicPackagePath, false);
        }
        catch (System.Exception e)
        {
            Debug.LogError("ASTRODASH PHASE9 FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("AstroDash/Fase 9 - Asignar música")]
    public static void Phase9FromMenu()
    {
        AssignMusic();   // en el editor el paquete se importa por menú Assets
        Debug.Log("ASTRODASH PHASE9 OK");
    }

    private static List<AudioClip> FindMusicClips()
    {
        var clips = new List<AudioClip>();
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;   // fuera lo de Packages/
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null) clips.Add(clip);
        }
        return clips;
    }

    private static void AssignMusic()
    {
        var clips = FindMusicClips();
        if (clips.Count == 0) throw new System.Exception("No hay AudioClips en Assets");

        // Elegir pista: preferir las marcadas como "loop"; si no hay,
        // la más larga (los jingles/stingers suelen ser cortos)
        AudioClip pick = null;
        foreach (var c in clips)
            if (c.name.ToLowerInvariant().Contains("loop")) { pick = c; break; }
        if (pick == null)
            foreach (var c in clips)
                if (pick == null || c.length > pick.length) pick = c;

        var log = new System.Text.StringBuilder("Pistas encontradas:\n");
        foreach (var c in clips) log.AppendLine($" - {c.name} ({c.length:0}s)");
        log.Append("Asignada al AudioManager: " + pick.name);
        Debug.Log(log.ToString());

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var audio = Object.FindFirstObjectByType<AudioManager>();
        if (audio == null) throw new System.Exception("No hay AudioManager en la escena (¿falta la Fase 8?)");
        SetField(audio, "musicClip", pick);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 10: sonido de disparo ======================
    // Cablea Assets/Audio/laser.wav (recortado y normalizado desde el
    // Laser.mp3 del usuario) al Shooter y baja un poco la música para
    // que los efectos respiren.

    public static void Phase10()
    {
        try { Phase10Work(); Debug.Log("ASTRODASH PHASE10 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE10 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 10 - Sonido de disparo")]
    public static void Phase10FromMenu()
    {
        Phase10Work();
        Debug.Log("ASTRODASH PHASE10 OK");
    }

    private static void Phase10Work()
    {
        const string clipPath = "Assets/Audio/laser.wav";
        AssetDatabase.ImportAsset(clipPath);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (clip == null) throw new FileNotFoundException("No se pudo cargar " + clipPath);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Disparo: clip en el Shooter (volumen sutil por defecto, 0.3)
        var shooter = Object.FindFirstObjectByType<Shooter>();
        SetField(shooter, "shotClip", clip);

        // Música un poco más baja para que los SFX se distingan
        var audio = Object.FindFirstObjectByType<AudioManager>();
        var so = new SerializedObject(audio);
        so.FindProperty("musicVolume").floatValue = 0.45f;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 11: menú, arte refinado y volumen ==========
    // Rocas rotas nuevas (fondo negro → transparencia real, sin halos
    // blancos), iconos de power-up SIN el círculo blanco interior + halo
    // sutil del color del tipo, y menú principal con botón JUGAR,
    // CONFIGURACIÓN y slider de volumen maestro en tiempo real.

    public static void Phase11()
    {
        try { Phase11Work(); Debug.Log("ASTRODASH PHASE11 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE11 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 11 - Menú y arte refinado")]
    public static void Phase11FromMenu()
    {
        Phase11Work();
        Debug.Log("ASTRODASH PHASE11 OK");
    }

    private static void Phase11Work()
    {
        // 1) Hojas re-procesadas: rotos (fondo negro) e iconos sin fondo
        WireBrokenRocks();
        WirePowerUpIcons();

        // 2) Prefab PowerUp: halo sutil detrás del icono
        var glowSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/circle.png");
        string prefabPath = $"{PrefabsDir}/PowerUp.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        root.GetComponent<SpriteRenderer>().sortingOrder = 2;   // icono sobre las rocas
        var oldGlow = root.transform.Find("Glow");
        if (oldGlow != null) Object.DestroyImmediate(oldGlow.gameObject);
        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(root.transform, false);
        glowGo.transform.localScale = Vector3.one * 1.7f;
        var glowSr = glowGo.AddComponent<SpriteRenderer>();
        glowSr.sprite = glowSprite;      // disco con caída radial de alpha
        glowSr.sortingOrder = 1;         // justo detrás del icono
        var puPrefabSo = new SerializedObject(root.GetComponent<PowerUpItem>());
        puPrefabSo.FindProperty("glow").objectReferenceValue = glowSr;
        puPrefabSo.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        // 3) Escena: menú principal + panel de configuración
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var startPanel = new SerializedObject(hud)
            .FindProperty("startPanel").objectReferenceValue as GameObject;

        // El texto "TOCA PARA JUGAR" se sustituye por botones de verdad
        DestroyChild(startPanel.transform, "PlayText");
        DestroyChild(startPanel.transform, "PlayButton");
        DestroyChild(startPanel.transform, "SettingsButton");
        var playBtn = MakeButton(startPanel.transform, "PlayButton", "JUGAR",
            new Color(0.13f, 0.83f, 0.93f), new Color(0.03f, 0.04f, 0.08f),
            new Vector2(0f, -560f), new Vector2(620f, 120f), 56f);
        var settingsBtn = MakeButton(startPanel.transform, "SettingsButton", "CONFIGURACIÓN",
            new Color(0.12f, 0.16f, 0.23f), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(0f, -710f), new Vector2(620f, 105f), 40f);

        // Panel de configuración (tapa la pantalla; oculto de inicio)
        DestroyChild(canvas.transform, "SettingsPanel");
        var settingsPanel = MakePanel(canvas.transform, "SettingsPanel",
            new Color(7 / 255f, 11 / 255f, 20 / 255f, 0.95f));
        MakeText(settingsPanel.transform, "TitleText", "CONFIGURACIÓN", 80, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 380f), new Vector2(1000f, 130f));
        MakeText(settingsPanel.transform, "VolumeLabel", "VOLUMEN", 46, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(700f, 80f));
        MakeText(settingsPanel.transform, "VolumeHint", "Música y efectos", 34, ColTextDim, false,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(700f, 60f));
        var slider = MakeVolumeSlider(settingsPanel.transform,
            new Vector2(0f, 35f), new Vector2(720f, 60f));
        var backBtn = MakeButton(settingsPanel.transform, "BackButton", "VOLVER",
            new Color(0.12f, 0.16f, 0.23f), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(0f, -640f), new Vector2(520f, 110f), 44f);
        settingsPanel.SetActive(false);

        // MenuController cableado en el Canvas
        var oldMenu = canvas.GetComponent<MenuController>();
        if (oldMenu != null) Object.DestroyImmediate(oldMenu);
        var menu = canvas.AddComponent<MenuController>();
        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        menuSo.FindProperty("playButton").objectReferenceValue = playBtn;
        menuSo.FindProperty("settingsButton").objectReferenceValue = settingsBtn;
        menuSo.FindProperty("backButton").objectReferenceValue = backBtn;
        menuSo.FindProperty("volumeSlider").objectReferenceValue = slider;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static Button MakeButton(Transform parent, string name, string label,
        Color bg, Color textColor, Vector2 pos, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = bg;
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        MakeText(go.transform, "Label", label, fontSize, textColor, true,
            new Vector2(0.5f, 0.5f), Vector2.zero, size);
        return button;
    }

    private static Slider MakeVolumeSlider(Transform parent, Vector2 pos, Vector2 size)
    {
        // DefaultControls arma el slider estándar (fondo + fill + handle)
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.name = "VolumeSlider";
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        // Colores del tema (sin sprites, los controles son rectángulos)
        go.transform.Find("Background").GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.23f);
        go.transform.Find("Fill Area/Fill").GetComponent<Image>().color = new Color(0.13f, 0.83f, 0.93f);
        go.transform.Find("Handle Slide Area/Handle").GetComponent<Image>().color = new Color(0.89f, 0.91f, 0.94f);

        var slider = go.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;   // el valor real lo pone MenuController al arrancar
        return slider;
    }

    // ================= FASE 12: audio de power-ups y mezcla ============
    // Sonidos de recogida (escudo con clip propio; los otros 4 usan los
    // 3 sonidos separados de "Resto de Power Ups"), aura de cápsulas
    // más sutil, y panel de configuración con sliders SEPARADOS de
    // música y efectos.

    public static void Phase12()
    {
        try { Phase12Work(); Debug.Log("ASTRODASH PHASE12 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE12 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 12 - Audio de power-ups y mezcla")]
    public static void Phase12FromMenu()
    {
        Phase12Work();
        Debug.Log("ASTRODASH PHASE12 OK");
    }

    private static void Phase12Work()
    {
        // --- Clips procesados por Python (recortados y normalizados) ---
        string[] clipPaths =
        {
            "Assets/Audio/shield_speed.wav",
            "Assets/Audio/pickup_1.wav",
            "Assets/Audio/pickup_2.wav",
            "Assets/Audio/pickup_3.wav",
        };
        var clips = new AudioClip[clipPaths.Length];
        for (int i = 0; i < clipPaths.Length; i++)
        {
            AssetDatabase.ImportAsset(clipPaths[i]);
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPaths[i]);
            if (clips[i] == null) throw new FileNotFoundException(clipPaths[i]);
        }
        var laserClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/laser.wav");

        // --- Prefab PowerUp: aura más pequeña (1.7 → 1.35) ---
        string prefabPath = $"{PrefabsDir}/PowerUp.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var glowT = root.transform.Find("Glow");
        if (glowT != null) glowT.localScale = Vector3.one * 1.35f;
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        // --- Escena ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Sonidos de recogida: escudo aparte; 3 sonidos para 4 tipos
        // (Imán=1, Doble=2, Rápido=3 y Potencia reutiliza el 1)
        var pp = Object.FindFirstObjectByType<PlayerPowerUps>();
        var ppSo = new SerializedObject(pp);
        ppSo.FindProperty("shieldPickupClip").objectReferenceValue = clips[0];
        var arr = ppSo.FindProperty("pickupClips");
        arr.arraySize = 4;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = clips[1];   // Imán
        arr.GetArrayElementAtIndex(1).objectReferenceValue = clips[2];   // Doble
        arr.GetArrayElementAtIndex(2).objectReferenceValue = clips[3];   // Rápido
        arr.GetArrayElementAtIndex(3).objectReferenceValue = clips[1];   // Potencia
        ppSo.ApplyModifiedPropertiesWithoutUndo();

        // Panel de configuración: quitar el slider único y poner dos
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var startPanel = new SerializedObject(hud)
            .FindProperty("startPanel").objectReferenceValue as GameObject;
        var settingsPanel = canvas.transform.Find("SettingsPanel").gameObject;

        foreach (var old in new[] { "VolumeLabel", "VolumeHint", "VolumeSlider",
                                    "MusicLabel", "MusicSlider", "SfxLabel", "SfxSlider" })
            DestroyChild(settingsPanel.transform, old);

        MakeText(settingsPanel.transform, "MusicLabel", "MÚSICA", 44, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(700f, 70f));
        var musicSlider = MakeVolumeSlider(settingsPanel.transform,
            new Vector2(0f, 125f), new Vector2(720f, 60f));
        musicSlider.gameObject.name = "MusicSlider";

        MakeText(settingsPanel.transform, "SfxLabel", "EFECTOS", 44, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(700f, 70f));
        var sfxSlider = MakeVolumeSlider(settingsPanel.transform,
            new Vector2(0f, -125f), new Vector2(720f, 60f));
        sfxSlider.gameObject.name = "SfxSlider";

        // MenuController re-cableado con los sliders nuevos
        var oldMenu = canvas.GetComponent<MenuController>();
        if (oldMenu != null) Object.DestroyImmediate(oldMenu);
        var menu = canvas.AddComponent<MenuController>();
        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        menuSo.FindProperty("playButton").objectReferenceValue =
            startPanel.transform.Find("PlayButton").GetComponent<Button>();
        menuSo.FindProperty("settingsButton").objectReferenceValue =
            startPanel.transform.Find("SettingsButton").GetComponent<Button>();
        menuSo.FindProperty("backButton").objectReferenceValue =
            settingsPanel.transform.Find("BackButton").GetComponent<Button>();
        menuSo.FindProperty("musicSlider").objectReferenceValue = musicSlider;
        menuSo.FindProperty("sfxSlider").objectReferenceValue = sfxSlider;
        menuSo.FindProperty("sfxPreviewClip").objectReferenceValue = laserClip;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 13: economía, naves y tienda ===============
    // Puntuación = distancia (los scripts ya lo hacen), estrellas como
    // moneda persistente, catálogo de 6 naves / 6 disparos / 3 fondos,
    // y pantalla unificada de Tienda+Inventario con pestañas.

    public static void Phase13()
    {
        try { Phase13Work(); Debug.Log("ASTRODASH PHASE13 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE13 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 13 - Economía y tienda")]
    public static void Phase13FromMenu()
    {
        Phase13Work();
        Debug.Log("ASTRODASH PHASE13 OK");
    }

    private static void Phase13Work()
    {
        // --- Hoja de naves nuevas: importar y cortar (ship_1..ship_5) ---
        var data = JsonUtility.FromJson<PuSliceData>(
            File.ReadAllText($"{SpritesDir}/ships_slices.json"));
        string shipsPath = $"{SpritesDir}/ships.png";
        AssetDatabase.ImportAsset(shipsPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(shipsPath);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = data.ppu;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;
        SliceSheet(imp, data.slices);

        var shipSprites = new Sprite[6];
        shipSprites[0] = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/nave.png");
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(shipsPath))
            if (obj is Sprite sp && sp.name.StartsWith("ship_"))
                shipSprites[int.Parse(sp.name.Substring(5))] = sp;
        for (int i = 0; i < 6; i++)
            if (shipSprites[i] == null) throw new System.Exception($"Falta el sprite de la nave {i}");

        var starSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/diamond.png");
        var circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/circle.png");

        // --- Escena ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Catálogo en el objeto GameManager
        var gm = Object.FindFirstObjectByType<GameManager>();
        var oldCat = gm.GetComponent<Catalog>();
        if (oldCat != null) Object.DestroyImmediate(oldCat);
        var catalog = gm.gameObject.AddComponent<Catalog>();
        WireCatalog(catalog, shipSprites);

        // Botón TIENDA en el menú (y reposicionar los otros dos)
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var hudSo = new SerializedObject(hud);
        var startPanel = hudSo.FindProperty("startPanel").objectReferenceValue as GameObject;
        ((RectTransform)startPanel.transform.Find("PlayButton").transform).anchoredPosition = new Vector2(0f, -520f);
        ((RectTransform)startPanel.transform.Find("SettingsButton").transform).anchoredPosition = new Vector2(0f, -790f);
        DestroyChild(startPanel.transform, "ShopButton");
        var shopBtn = MakeButton(startPanel.transform, "ShopButton", "TIENDA",
            new Color(0.98f, 0.75f, 0.14f), new Color(0.1f, 0.07f, 0.02f),
            new Vector2(0f, -655f), new Vector2(620f, 110f), 46f);

        // Panel de TIENDA (encima de todo, oculto de inicio)
        DestroyChild(canvas.transform, "ShopPanel");
        var shopPanel = MakePanel(canvas.transform, "ShopPanel",
            new Color(7 / 255f, 11 / 255f, 20 / 255f, 0.97f));
        MakeText(shopPanel.transform, "TitleText", "TIENDA", 72, ColText, true,
            new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(600f, 110f));

        // Saldo: icono de estrella + número (arriba a la derecha)
        var balance = new GameObject("Balance", typeof(RectTransform));
        balance.transform.SetParent(shopPanel.transform, false);
        var brt = (RectTransform)balance.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(-40f, -60f);
        brt.sizeDelta = new Vector2(280f, 80f);
        var starImg = new GameObject("Icon", typeof(RectTransform));
        starImg.transform.SetParent(balance.transform, false);
        var sirt = (RectTransform)starImg.transform;
        sirt.anchorMin = sirt.anchorMax = sirt.pivot = new Vector2(0f, 0.5f);
        sirt.anchoredPosition = new Vector2(0f, 0f);
        sirt.sizeDelta = new Vector2(64f, 64f);
        starImg.AddComponent<Image>().sprite = starSprite;
        var starsText = MakeText(balance.transform, "StarsText", "0", 52, ColItem, true,
            new Vector2(0f, 0.5f), new Vector2(200f, 0f), new Vector2(240f, 80f));
        starsText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

        // Pestañas
        var tabShips = MakeButton(shopPanel.transform, "TabShips", "NAVES",
            TabColor(true), new Color(0.03f, 0.04f, 0.08f),
            new Vector2(-350f, 330f), new Vector2(330f, 90f), 34f);
        var tabShots = MakeButton(shopPanel.transform, "TabShots", "DISPAROS",
            TabColor(false), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(0f, 330f), new Vector2(330f, 90f), 34f);
        var tabBgs = MakeButton(shopPanel.transform, "TabBackgrounds", "FONDOS",
            TabColor(false), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(350f, 330f), new Vector2(330f, 90f), 34f);
        // (las pestañas están ancladas al centro: convertir a coordenadas de centro)
        ((RectTransform)tabShips.transform).anchoredPosition = new Vector2(-350f, 330f);
        ((RectTransform)tabShots.transform).anchoredPosition = new Vector2(0f, 330f);
        ((RectTransform)tabBgs.transform).anchoredPosition = new Vector2(350f, 330f);

        // Contenedor de filas (las filas las genera ShopController)
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(shopPanel.transform, false);
        var crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0.04f, 0.5f);
        crt.anchorMax = new Vector2(0.96f, 0.5f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = new Vector2(0f, 270f);
        crt.sizeDelta = new Vector2(0f, 920f);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var closeBtn = MakeButton(shopPanel.transform, "CloseButton", "VOLVER",
            new Color(0.12f, 0.16f, 0.23f), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(0f, -820f), new Vector2(520f, 100f), 42f);
        shopPanel.SetActive(false);

        // ShopController cableado en el Canvas
        var oldShop = canvas.GetComponent<ShopController>();
        if (oldShop != null) Object.DestroyImmediate(oldShop);
        var shop = canvas.AddComponent<ShopController>();
        var shopSo = new SerializedObject(shop);
        shopSo.FindProperty("shopPanel").objectReferenceValue = shopPanel;
        shopSo.FindProperty("shopButton").objectReferenceValue = shopBtn;
        shopSo.FindProperty("closeButton").objectReferenceValue = closeBtn;
        shopSo.FindProperty("shipsTabButton").objectReferenceValue = tabShips;
        shopSo.FindProperty("shotsTabButton").objectReferenceValue = tabShots;
        shopSo.FindProperty("backgroundsTabButton").objectReferenceValue = tabBgs;
        shopSo.FindProperty("content").objectReferenceValue = crt;
        shopSo.FindProperty("starsText").objectReferenceValue = starsText;
        shopSo.FindProperty("starIcon").objectReferenceValue = starSprite;
        shopSo.FindProperty("circleIcon").objectReferenceValue = circleSprite;
        shopSo.ApplyModifiedPropertiesWithoutUndo();

        // HUD: contador de estrellas de la partida (arriba a la derecha)
        DestroyChild(canvas.transform, "StarsGroup");
        var group = new GameObject("StarsGroup", typeof(RectTransform));
        group.transform.SetParent(canvas.transform, false);
        var grt = (RectTransform)group.transform;
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1f, 1f);
        grt.anchoredPosition = new Vector2(-40f, -70f);
        grt.sizeDelta = new Vector2(230f, 70f);
        var gIcon = new GameObject("Icon", typeof(RectTransform));
        gIcon.transform.SetParent(group.transform, false);
        var girt = (RectTransform)gIcon.transform;
        girt.anchorMin = girt.anchorMax = girt.pivot = new Vector2(0f, 0.5f);
        girt.anchoredPosition = Vector2.zero;
        girt.sizeDelta = new Vector2(56f, 56f);
        gIcon.AddComponent<Image>().sprite = starSprite;
        var hudStars = MakeText(group.transform, "StarsText", "0", 46, ColItem, true,
            new Vector2(0f, 0.5f), new Vector2(170f, 0f), new Vector2(180f, 70f));
        hudStars.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        group.SetActive(false);

        // Game over: estrellas ganadas
        var gameOverPanel = hudSo.FindProperty("gameOverPanel").objectReferenceValue as GameObject;
        DestroyChild(gameOverPanel.transform, "StarsEarnedText");
        var goStars = MakeText(gameOverPanel.transform, "StarsEarnedText", "+0", 46, ColItem, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(600f, 80f));

        hudSo.FindProperty("starsGroup").objectReferenceValue = group;
        hudSo.FindProperty("starsText").objectReferenceValue = hudStars;
        hudSo.FindProperty("gameOverStarsText").objectReferenceValue = goStars;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static Color TabColor(bool active) =>
        active ? new Color(0.13f, 0.83f, 0.93f) : new Color(0.12f, 0.16f, 0.23f);

    private static void WireCatalog(Catalog catalog, Sprite[] shipSprites)
    {
        // Paleta compartida entre naves (color "de diseño") y disparos
        Color cian = Color.white;   // el disparo por defecto ya es cian en textura
        Color amarillo = new Color(0.99f, 0.88f, 0.28f);
        Color verde = new Color(0.29f, 0.87f, 0.50f);
        Color azul = new Color(0.23f, 0.51f, 0.96f);
        Color morado = new Color(0.65f, 0.55f, 0.98f);
        Color rojo = new Color(0.97f, 0.44f, 0.44f);

        var so = new SerializedObject(catalog);

        var ships = so.FindProperty("ships");
        // Nombres creativos en inglés (Fase 14): clásica, amarilla, tanque
        // verde, azul futurista, morada araña, roja futurista — en ese orden
        string[] shipNames = { "Starblazer", "Sunflare", "Juggernaut",
                               "Phantom", "Void Weaver", "Crimson Comet" };
        int[] shipPrices = { 0, 100, 150, 200, 250, 300 };
        Color[] shipColors = { cian, amarillo, verde, azul, morado, rojo };
        ships.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            var e = ships.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = $"ship_{i}";
            e.FindPropertyRelative("displayName").stringValue = shipNames[i];
            e.FindPropertyRelative("price").intValue = shipPrices[i];
            e.FindPropertyRelative("sprite").objectReferenceValue = shipSprites[i];
            e.FindPropertyRelative("designBulletColor").colorValue = shipColors[i];
        }

        var shots = so.FindProperty("shots");
        string[] shotNames = { "Cyan Plasma", "Solar Round", "Emerald Bolt",
                               "Sapphire Ray", "Violet Pulse", "Crimson Blast" };
        int[] shotPrices = { 0, 50, 80, 80, 100, 120 };
        Color[] shotColors = { cian, amarillo, verde, azul, morado, rojo };
        shots.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            var e = shots.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = $"shot_{i}";
            e.FindPropertyRelative("displayName").stringValue = shotNames[i];
            e.FindPropertyRelative("price").intValue = shotPrices[i];
            e.FindPropertyRelative("color").colorValue = shotColors[i];
        }

        var bgs = so.FindProperty("backgrounds");
        string[] bgNames = { "Deep Space", "Purple Nebula", "Emerald Abyss" };
        int[] bgPrices = { 0, 120, 120 };
        Color[] bgColors =
        {
            new Color(7 / 255f, 11 / 255f, 20 / 255f),
            new Color(20 / 255f, 7 / 255f, 32 / 255f),
            new Color(4 / 255f, 20 / 255f, 15 / 255f),
        };
        bgs.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            var e = bgs.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = $"bg_{i}";
            e.FindPropertyRelative("displayName").stringValue = bgNames[i];
            e.FindPropertyRelative("price").intValue = bgPrices[i];
            e.FindPropertyRelative("cameraColor").colorValue = bgColors[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================= FASE 14: pausa, nombres y estilo arcade =========
    // Nombres en inglés (re-cablea el catálogo), icono limpio de bala
    // para la tienda, modo prueba (todo desbloqueado, ver GameConfig),
    // sistema de PAUSA con Time.timeScale, y esquinas redondeadas
    // 9-slice en todos los botones y filas (estilo arcade moderno).

    public static void Phase14()
    {
        try { Phase14Work(); Debug.Log("ASTRODASH PHASE14 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE14 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 14 - Pausa y estilo")]
    public static void Phase14FromMenu()
    {
        Phase14Work();
        Debug.Log("ASTRODASH PHASE14 OK");
    }

    private static void Phase14Work()
    {
        // --- Sprites de UI nuevos ---
        MakeSpriteRGBA("rounded", ShadeRoundedPanel);
        var roundedImp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/rounded.png");
        roundedImp.spriteBorder = new Vector4(26f, 26f, 26f, 26f);   // bordes 9-slice
        roundedImp.SaveAndReimport();
        var rounded = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded.png");

        var bulletUi = MakeSpriteRGBA("bullet_ui", ShadeCleanBullet);

        // --- Escena ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Nombres nuevos del catálogo (misma estructura, solo re-cablear)
        var gm = Object.FindFirstObjectByType<GameManager>();
        var catalog = gm.GetComponent<Catalog>();
        var shipSprites = new Sprite[6];
        for (int i = 0; i < 6; i++) shipSprites[i] = catalog.ships[i].sprite;
        WireCatalog(catalog, shipSprites);

        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;

        // --- Pausa: botón "II" (arriba-izquierda) + panel ---
        DestroyChild(canvas.transform, "PauseButton");
        DestroyChild(canvas.transform, "PausePanel");
        var pauseBtn = MakeButton(canvas.transform, "PauseButton", "II",
            new Color(0.12f, 0.16f, 0.23f, 0.85f), new Color(0.89f, 0.91f, 0.94f),
            Vector2.zero, new Vector2(110f, 90f), 40f);
        var pbrt = (RectTransform)pauseBtn.transform;
        pbrt.anchorMin = pbrt.anchorMax = pbrt.pivot = new Vector2(0f, 1f);
        pbrt.anchoredPosition = new Vector2(40f, -60f);

        var pausePanel = MakePanel(canvas.transform, "PausePanel",
            new Color(7 / 255f, 11 / 255f, 20 / 255f, 0.9f));
        MakeText(pausePanel.transform, "TitleText", "PAUSED", 90, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(800f, 140f));
        var resumeBtn = MakeButton(pausePanel.transform, "ResumeButton", "RESUME",
            new Color(0.13f, 0.83f, 0.93f), new Color(0.03f, 0.04f, 0.08f),
            new Vector2(0f, 20f), new Vector2(620f, 120f), 48f);
        var quitBtn = MakeButton(pausePanel.transform, "QuitButton", "QUIT TO MAIN MENU",
            new Color(0.12f, 0.16f, 0.23f), new Color(0.89f, 0.91f, 0.94f),
            new Vector2(0f, -140f), new Vector2(620f, 110f), 38f);
        pausePanel.SetActive(false);

        var oldPause = canvas.GetComponent<PauseController>();
        if (oldPause != null) Object.DestroyImmediate(oldPause);
        var pause = canvas.AddComponent<PauseController>();
        var pauseSo = new SerializedObject(pause);
        pauseSo.FindProperty("pausePanel").objectReferenceValue = pausePanel;
        pauseSo.FindProperty("pauseButton").objectReferenceValue = pauseBtn;
        pauseSo.FindProperty("resumeButton").objectReferenceValue = resumeBtn;
        pauseSo.FindProperty("quitButton").objectReferenceValue = quitBtn;
        pauseSo.ApplyModifiedPropertiesWithoutUndo();

        // --- Estilo arcade: esquinas redondeadas en TODOS los botones
        //     (menú, tienda, configuración, pausa; incluye inactivos) ---
        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
        {
            if (button.image == null) continue;
            button.image.sprite = rounded;
            button.image.type = Image.Type.Sliced;
        }

        // --- Tienda: icono limpio de bala + filas redondeadas ---
        var shop = canvas.GetComponent<ShopController>();
        var shopSo = new SerializedObject(shop);
        shopSo.FindProperty("bulletIcon").objectReferenceValue = bulletUi;
        shopSo.FindProperty("roundedSprite").objectReferenceValue = rounded;
        shopSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // Rectángulo redondeado blanco para 9-slice (radio 40 px en 256 —
    // más curvo que el mockup, a petición del usuario)
    private static Color ShadeRoundedPanel(float u, float v)
    {
        const float half = 0.5f;
        float radius = roundedRadius;   // 40/256 normal; 55/256 en modo píldora
        float ax = Mathf.Abs(u), ay = Mathf.Abs(v);
        float dx = Mathf.Max(0f, ax - (half - radius));
        float dy = Mathf.Max(0f, ay - (half - radius));
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        // Borde con antialias de ~1.5 px
        float a = Mathf.Clamp01((radius - d) / 0.006f);
        return new Color(1f, 1f, 1f, a);
    }

    // Bala "limpia" para la UI de la tienda: cápsula sólida sin estela
    private static Color ShadeCleanBullet(float u, float v)
    {
        const float halfW = 0.15f, straight = 0.26f;
        float dy = Mathf.Max(0f, Mathf.Abs(v) - straight);
        float d = Mathf.Sqrt(u * u + dy * dy);
        float a = Mathf.Clamp01((halfW - d) / 0.01f);   // borde suavizado
        return new Color(1f, 1f, 1f, a);
    }

    // ================= FASE 15: design system del handoff ==============
    // Aplica el diseño de Claude Design (claude.ai/design): paleta
    // #0B1120, botones con gradiente cian/dorado y radios de 16px,
    // píldora de estrellas, pestañas segmented-control, filas con caja
    // de icono, 3 sliders con porcentaje, y tipografías Space Grotesk
    // (títulos/botones) + Rubik (cuerpo).

    private static readonly Color DsCyan = new Color(0.247f, 0.878f, 0.937f);     // #3FE0EF
    private static readonly Color DsCyanText = new Color(0.016f, 0.078f, 0.102f); // #04141A
    private static readonly Color DsGold = new Color(1f, 0.82f, 0.4f);            // #FFD166
    private static readonly Color DsGoldText = new Color(0.227f, 0.141f, 0f);     // #3A2400
    private static readonly Color DsStar = new Color(1f, 0.808f, 0.329f);         // #FFCE54
    private static readonly Color DsTitle = new Color(0.949f, 0.965f, 0.984f);    // #F2F6FB
    private static readonly Color DsTextDim = new Color(1f, 1f, 1f, 0.45f);
    private static readonly Color DsNeutral = new Color(0.863f, 0.894f, 0.933f);  // #DCE4EE
    private static readonly Color DsPanel = new Color(0.043f, 0.067f, 0.125f, 0.97f); // #0B1120

    public static void Phase15()
    {
        try { Phase15Work(); Debug.Log("ASTRODASH PHASE15 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE15 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 15 - Design system")]
    public static void Phase15FromMenu()
    {
        Phase15Work();
        Debug.Log("ASTRODASH PHASE15 OK");
    }

    private static void Phase15Work()
    {
        // --- Sprites del design system ---
        MakeSpriteRGBA("rounded_grad", ShadeRoundedGrad);
        SetBorder("rounded_grad", 26f);
        MakeSpriteRGBA("rounded_line", ShadeRoundedLine);
        SetBorder("rounded_line", 26f);
        MakeSpriteRGBA("pill", ShadePill);
        SetBorder("pill", 44f);
        var star5 = MakeSpriteRGBA("star5", ShadeStar5);
        MakeSpriteRGBA("knob", ShadeKnob);

        var rounded = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded.png");
        var grad = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded_grad.png");
        var line = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded_line.png");
        var pill = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill.png");
        var knob = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/knob.png");

        // --- Tipografías (creadas desde los TTF descargados) ---
        var sg = MakeFontAsset("Assets/Fonts/SpaceGrotesk-Bold.ttf", "Assets/Fonts/SpaceGrotesk SDF.asset");
        var rubik = MakeFontAsset("Assets/Fonts/Rubik-Medium.ttf", "Assets/Fonts/Rubik SDF.asset");

        // --- Escena ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var hudSo = new SerializedObject(hud);
        var startPanel = hudSo.FindProperty("startPanel").objectReferenceValue as GameObject;
        var settingsPanel = canvas.transform.Find("SettingsPanel").gameObject;
        var shopPanel = canvas.transform.Find("ShopPanel").gameObject;
        var pausePanel = canvas.transform.Find("PausePanel").gameObject;

        // Tipografías: Space Grotesk para todo; Rubik en textos de cuerpo
        if (sg != null)
            foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                t.font = (rubik != null && (t.name == "HintText" || t.name == "VolumeHint"))
                    ? rubik : sg;

        // ---- MENÚ PRINCIPAL ----
        var title = startPanel.transform.Find("TitleText").GetComponent<TextMeshProUGUI>();
        title.color = DsTitle;
        title.characterSpacing = 6f;
        var hint = startPanel.transform.Find("HintText").GetComponent<TextMeshProUGUI>();
        hint.color = DsTextDim;
        var best = startPanel.transform.Find("BestText").GetComponent<TextMeshProUGUI>();
        best.color = DsStar;
        ((RectTransform)best.transform).anchoredPosition = new Vector2(0f, -395f);

        StyleGradientButton(startPanel.transform.Find("PlayButton").GetComponent<Button>(),
            grad, DsCyan, DsCyanText);
        StyleGradientButton(startPanel.transform.Find("ShopButton").GetComponent<Button>(),
            grad, DsGold, DsGoldText);
        StyleOutlineButton(startPanel.transform.Find("SettingsButton").GetComponent<Button>(),
            rounded, line, DsNeutral);

        // Píldora de estrellas (arriba a la derecha, saldo total)
        DestroyChild(startPanel.transform, "MenuStars");
        var pillGo = new GameObject("MenuStars", typeof(RectTransform));
        pillGo.transform.SetParent(startPanel.transform, false);
        var prt = (RectTransform)pillGo.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
        prt.anchoredPosition = new Vector2(-40f, -55f);
        prt.sizeDelta = new Vector2(230f, 84f);
        var pillImg = pillGo.AddComponent<Image>();
        pillImg.sprite = pill;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color(1f, 1f, 1f, 0.07f);
        var pIcon = new GameObject("Icon", typeof(RectTransform));
        pIcon.transform.SetParent(pillGo.transform, false);
        var pirt = (RectTransform)pIcon.transform;
        pirt.anchorMin = pirt.anchorMax = pirt.pivot = new Vector2(0f, 0.5f);
        pirt.anchoredPosition = new Vector2(28f, 0f);
        pirt.sizeDelta = new Vector2(44f, 44f);
        var pIconImg = pIcon.AddComponent<Image>();
        pIconImg.sprite = star5;
        pIconImg.color = DsStar;
        var pillText = MakeText(pillGo.transform, "StarsText", "0", 40, DsStar, true,
            new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(140f, 84f));
        pillText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        if (sg != null) pillText.font = sg;

        // ---- CONFIGURACIÓN: 3 sliders con porcentaje ----
        foreach (var old in new[] { "VolumeLabel", "VolumeHint", "VolumeSlider",
                                    "MusicLabel", "MusicSlider", "SfxLabel", "SfxSlider",
                                    "MasterGroup", "MusicGroup", "SfxGroup" })
            DestroyChild(settingsPanel.transform, old);
        settingsPanel.GetComponent<Image>().color = DsPanel;

        var masterUi = MakeSliderGroup(settingsPanel.transform, "MasterGroup",
            "VOLUMEN GENERAL", 260f, rounded, knob, sg);
        var musicUi = MakeSliderGroup(settingsPanel.transform, "MusicGroup",
            "MÚSICA", 40f, rounded, knob, sg);
        var sfxUi = MakeSliderGroup(settingsPanel.transform, "SfxGroup",
            "EFECTOS", -180f, rounded, knob, sg);

        var backBtn = settingsPanel.transform.Find("BackButton").GetComponent<Button>();
        StyleGradientButton(backBtn, grad, DsCyan, DsCyanText);

        // MenuController re-cableado (campos nuevos)
        var oldMenu = canvas.GetComponent<MenuController>();
        if (oldMenu != null) Object.DestroyImmediate(oldMenu);
        var menu = canvas.AddComponent<MenuController>();
        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
        menuSo.FindProperty("playButton").objectReferenceValue =
            startPanel.transform.Find("PlayButton").GetComponent<Button>();
        menuSo.FindProperty("settingsButton").objectReferenceValue =
            startPanel.transform.Find("SettingsButton").GetComponent<Button>();
        menuSo.FindProperty("backButton").objectReferenceValue = backBtn;
        menuSo.FindProperty("masterSlider").objectReferenceValue = masterUi.slider;
        menuSo.FindProperty("masterValueText").objectReferenceValue = masterUi.value;
        menuSo.FindProperty("musicSlider").objectReferenceValue = musicUi.slider;
        menuSo.FindProperty("musicValueText").objectReferenceValue = musicUi.value;
        menuSo.FindProperty("sfxSlider").objectReferenceValue = sfxUi.slider;
        menuSo.FindProperty("sfxValueText").objectReferenceValue = sfxUi.value;
        menuSo.FindProperty("sfxPreviewClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/laser.wav");
        menuSo.FindProperty("menuStarsText").objectReferenceValue = pillText;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- TIENDA ----
        shopPanel.GetComponent<Image>().color = DsPanel;
        var balance = shopPanel.transform.Find("Balance");
        var balImg = balance.GetComponent<Image>() ?? balance.gameObject.AddComponent<Image>();
        balImg.sprite = pill;
        balImg.type = Image.Type.Sliced;
        balImg.color = new Color(1f, 1f, 1f, 0.07f);
        var balIcon = balance.Find("Icon").GetComponent<Image>();
        balIcon.sprite = star5;
        balIcon.color = DsStar;
        balance.Find("StarsText").GetComponent<TextMeshProUGUI>().color = DsStar;

        // Barra contenedora de pestañas (segmented control)
        DestroyChild(shopPanel.transform, "TabBar");
        var tabBar = new GameObject("TabBar", typeof(RectTransform));
        tabBar.transform.SetParent(shopPanel.transform, false);
        tabBar.transform.SetSiblingIndex(2);   // detrás de las pestañas
        var tbrt = (RectTransform)tabBar.transform;
        tbrt.anchorMin = tbrt.anchorMax = tbrt.pivot = new Vector2(0.5f, 0.5f);
        tbrt.anchoredPosition = new Vector2(0f, 330f);
        tbrt.sizeDelta = new Vector2(1030f, 112f);
        var tabBarImg = tabBar.AddComponent<Image>();
        tabBarImg.sprite = rounded;
        tabBarImg.type = Image.Type.Sliced;
        tabBarImg.color = new Color(1f, 1f, 1f, 0.04f);

        StyleOutlineButton(shopPanel.transform.Find("CloseButton").GetComponent<Button>(),
            rounded, line, DsNeutral);

        var shop = canvas.GetComponent<ShopController>();
        var shopSo = new SerializedObject(shop);
        shopSo.FindProperty("starIcon").objectReferenceValue = star5;
        shopSo.FindProperty("gradientSprite").objectReferenceValue = grad;
        shopSo.FindProperty("roundedSprite").objectReferenceValue = rounded;
        if (sg != null) shopSo.FindProperty("headingFont").objectReferenceValue = sg;
        if (rubik != null) shopSo.FindProperty("bodyFont").objectReferenceValue = rubik;
        shopSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- PAUSA ----
        pausePanel.GetComponent<Image>().color = DsPanel;
        StyleGradientButton(pausePanel.transform.Find("ResumeButton").GetComponent<Button>(),
            grad, DsCyan, DsCyanText);
        StyleOutlineButton(pausePanel.transform.Find("QuitButton").GetComponent<Button>(),
            rounded, line, DsNeutral);
        StyleOutlineButton(canvas.transform.Find("PauseButton").GetComponent<Button>(),
            rounded, line, DsNeutral);

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ------------------- Helpers del design system -------------------

    private static void SetBorder(string name, float border)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
        imp.spriteBorder = new Vector4(border, border, border, border);
        imp.SaveAndReimport();
    }

    private static TMP_FontAsset MakeFontAsset(string ttfPath, string assetPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null) return existing;
        var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null) { Debug.LogWarning("Sin TTF: " + ttfPath); return null; }
        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
        if (fa == null) return null;
        AssetDatabase.CreateAsset(fa, assetPath);
        fa.atlasTextures[0].name = fa.name + " Atlas";
        fa.material.name = fa.name + " Material";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
    }

    private static void StyleGradientButton(Button button, Sprite grad, Color tint, Color textColor)
    {
        button.image.sprite = grad;
        button.image.type = Image.Type.Sliced;
        button.image.color = tint;
        DestroyChild(button.transform, "Border");
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = textColor;
    }

    private static void StyleOutlineButton(Button button, Sprite rounded, Sprite line, Color textColor)
    {
        button.image.sprite = rounded;
        button.image.type = Image.Type.Sliced;
        button.image.color = new Color(1f, 1f, 1f, 0.05f);
        DestroyChild(button.transform, "Border");
        var borderGo = new GameObject("Border", typeof(RectTransform));
        borderGo.transform.SetParent(button.transform, false);
        var brt = (RectTransform)borderGo.transform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        var bimg = borderGo.AddComponent<Image>();
        bimg.sprite = line;
        bimg.type = Image.Type.Sliced;
        bimg.color = new Color(1f, 1f, 1f, 0.14f);
        bimg.raycastTarget = false;
        var label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = textColor;
    }

    private struct SliderUi { public Slider slider; public TextMeshProUGUI value; }

    // Grupo del panel de configuración: etiqueta + "NN%" + slider estilizado
    private static SliderUi MakeSliderGroup(Transform parent, string name, string label,
        float y, Sprite rounded, Sprite knob, TMP_FontAsset font)
    {
        var group = new GameObject(name, typeof(RectTransform));
        group.transform.SetParent(parent, false);
        var grt = (RectTransform)group.transform;
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(0f, y);
        grt.sizeDelta = new Vector2(780f, 130f);

        var lab = MakeText(group.transform, "Label", label, 36, DsNeutral, true,
            new Vector2(0f, 1f), new Vector2(200f, -25f), new Vector2(420f, 50f));
        lab.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        if (font != null) lab.font = font;

        var val = MakeText(group.transform, "Value", "100%", 32,
            new Color(0.161f, 0.839f, 0.910f), true,   // #29D6E8
            new Vector2(1f, 1f), new Vector2(-90f, -25f), new Vector2(180f, 50f));
        val.alignment = TMPro.TextAlignmentOptions.MidlineRight;
        if (font != null) val.font = font;

        var slider = MakeVolumeSlider(group.transform, new Vector2(0f, -35f), new Vector2(780f, 40f));
        // Estilo del handoff: pista fina, relleno cian, knob circular claro
        var bg = slider.transform.Find("Background").GetComponent<Image>();
        bg.sprite = rounded; bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.12f);
        var bgRt = (RectTransform)bg.transform;
        bgRt.anchorMin = new Vector2(0f, 0.5f); bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 14f);
        var fill = slider.transform.Find("Fill Area/Fill").GetComponent<Image>();
        fill.sprite = rounded; fill.type = Image.Type.Sliced;
        fill.color = new Color(0.161f, 0.839f, 0.910f);   // #29D6E8
        var fillArea = (RectTransform)slider.transform.Find("Fill Area");
        fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.sizeDelta = new Vector2(-20f, 14f);
        var handle = slider.transform.Find("Handle Slide Area/Handle").GetComponent<Image>();
        handle.sprite = knob;
        handle.color = new Color(0.933f, 0.957f, 0.984f);   // #EEF4FB
        ((RectTransform)handle.transform).sizeDelta = new Vector2(48f, 48f);

        return new SliderUi { slider = slider, value = val };
    }

    private static float RoundedSdf(float u, float v, float halfW, float halfH, float radius)
    {
        float qx = Mathf.Abs(u) - (halfW - radius);
        float qy = Mathf.Abs(v) - (halfH - radius);
        float ox = Mathf.Max(qx, 0f);
        float oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    // Redondeado con gradiente vertical sutil (se tiñe del color del botón)
    private static Color ShadeRoundedGrad(float u, float v)
    {
        float sdf = RoundedSdf(u, v, 0.5f, 0.5f, roundedRadius);
        float a = Mathf.Clamp01(-sdf / 0.006f);
        float bright = 1f - 0.22f * (0.5f - v);   // claro arriba → oscuro abajo
        return new Color(bright, bright, bright, a);
    }

    // Solo el borde del redondeado (para botones "outline")
    private static Color ShadeRoundedLine(float u, float v)
    {
        float sdf = RoundedSdf(u, v, 0.5f, 0.5f, roundedRadius);
        const float t = 0.018f;
        float a = Mathf.Clamp01((sdf + t) / 0.005f) * Mathf.Clamp01(-sdf / 0.005f);
        return new Color(1f, 1f, 1f, a);
    }

    // Cápsula horizontal (píldora del saldo)
    private static Color ShadePill(float u, float v)
    {
        float sdf = RoundedSdf(u, v, 0.49f, 0.18f, 0.18f);
        return new Color(1f, 1f, 1f, Mathf.Clamp01(-sdf / 0.006f));
    }

    // Estrella de 5 puntas (el clip-path del handoff, test par-impar)
    private static readonly Vector2[] Star5Pts =
    {
        new Vector2(0.00f, 0.50f), new Vector2(0.11f, 0.15f), new Vector2(0.48f, 0.15f),
        new Vector2(0.18f, -0.07f), new Vector2(0.29f, -0.41f), new Vector2(0.00f, -0.20f),
        new Vector2(-0.29f, -0.41f), new Vector2(-0.18f, -0.07f), new Vector2(-0.48f, 0.15f),
        new Vector2(-0.11f, 0.15f),
    };

    private static Color ShadeStar5(float u, float v)
    {
        bool inside = false;
        for (int i = 0, j = Star5Pts.Length - 1; i < Star5Pts.Length; j = i++)
        {
            Vector2 a = Star5Pts[i], b = Star5Pts[j];
            if ((a.y > v) != (b.y > v) &&
                u < (b.x - a.x) * (v - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside ? Color.white : Color.clear;
    }

    // Círculo sólido (knob de los sliders)
    private static Color ShadeKnob(float u, float v)
    {
        float d = Mathf.Sqrt(u * u + v * v);
        return new Color(1f, 1f, 1f, Mathf.Clamp01((0.45f - d) / 0.012f));
    }

    // ================= FASE 16: pulido visual del menú =================
    // Esquinas redondeadas de verdad (los sprites de UI se comprimían
    // con DXT y las esquinas salían dentadas → sin compresión, y radio
    // 26→32 px como el mockup), nave oculta fuera de partida (el script
    // ya lo hace) y cohetito sobre el título como en el diseño.

    public static void Phase16()
    {
        try { Phase16Work(); Debug.Log("ASTRODASH PHASE16 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE16 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 16 - Pulido del menú")]
    public static void Phase16FromMenu()
    {
        Phase16Work();
        Debug.Log("ASTRODASH PHASE16 OK");
    }

    private static void Phase16Work()
    {
        // --- Regenerar los redondeados con radio 32 y sin compresión ---
        MakeSpriteRGBA("rounded", ShadeRoundedPanel);
        SetBorder("rounded", 32f);
        MakeSpriteRGBA("rounded_grad", ShadeRoundedGrad);
        SetBorder("rounded_grad", 32f);
        MakeSpriteRGBA("rounded_line", ShadeRoundedLine);
        SetBorder("rounded_line", 32f);

        // --- SIN compresión para TODOS los sprites (los ya importados
        //     también sufrían artefactos DXT en bordes suaves) ---
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SpritesDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            if (imp.textureCompression != TextureImporterCompression.Uncompressed)
            {
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
        }

        // --- Cohetito sobre el título (como el mockup) ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var startPanel = new SerializedObject(hud)
            .FindProperty("startPanel").objectReferenceValue as GameObject;
        DestroyChild(startPanel.transform, "TitleIcon");
        var iconGo = new GameObject("TitleIcon", typeof(RectTransform));
        iconGo.transform.SetParent(startPanel.transform, false);
        var irt = (RectTransform)iconGo.transform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(0f, 470f);
        irt.sizeDelta = new Vector2(90f, 130f);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/nave.png");
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 17: radios reales y transición =============
    // Causa raíz de los botones "poco redondos": el Canvas escala los
    // bordes 9-slice por (referencePixelsPerUnit 100 / PPU del sprite).
    // Con PPU 256, el borde de 32 px se dibujaba a 12.5 px. Se ponen los
    // sprites 9-slice a PPU 100. Además: el icono del título pasa a ser
    // la nave EQUIPADA y se añade la transición de inicio (el menú se
    // desvanece y la nave baja volando — código en MenuController y
    // PlayerController).

    public static void Phase17()
    {
        try { Phase17Work(); Debug.Log("ASTRODASH PHASE17 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE17 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 17 - Radios y transición")]
    public static void Phase17FromMenu()
    {
        Phase17Work();
        Debug.Log("ASTRODASH PHASE17 OK");
    }

    private static void Phase17Work()
    {
        // PPU 100 en los sprites 9-slice para que el borde se dibuje 1:1
        foreach (var name in new[] { "rounded", "rounded_grad", "rounded_line", "pill" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
            imp.spritePixelsPerUnit = 100f;
            imp.SaveAndReimport();
        }

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var startPanel = new SerializedObject(hud)
            .FindProperty("startPanel").objectReferenceValue as GameObject;

        // CanvasGroup para poder desvanecer el panel al pulsar JUGAR
        var group = startPanel.GetComponent<CanvasGroup>();
        if (group == null) group = startPanel.AddComponent<CanvasGroup>();

        var titleIcon = startPanel.transform.Find("TitleIcon").GetComponent<Image>();

        var menu = canvas.GetComponent<MenuController>();
        var menuSo = new SerializedObject(menu);
        menuSo.FindProperty("startPanelGroup").objectReferenceValue = group;
        menuSo.FindProperty("titleShipIcon").objectReferenceValue = titleIcon;
        menuSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 19: radio 40 px ============================
    // Regenera los tres redondeados con radio 40 (antes 32): botones
    // claramente curvos. Los sprites conservan GUID → todo se actualiza.

    public static void Phase19()
    {
        try { Phase19Work(); Debug.Log("ASTRODASH PHASE19 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE19 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 19 - Radio 40")]
    public static void Phase19FromMenu()
    {
        Phase19Work();
        Debug.Log("ASTRODASH PHASE19 OK");
    }

    private static void Phase19Work()
    {
        MakeSpriteRGBA("rounded", ShadeRoundedPanel);
        SetBorder("rounded", 40f);
        MakeSpriteRGBA("rounded_grad", ShadeRoundedGrad);
        SetBorder("rounded_grad", 40f);
        MakeSpriteRGBA("rounded_line", ShadeRoundedLine);
        SetBorder("rounded_line", 40f);
        // PPU 100 (la Fase 17 lo fijó; MakeSpriteRGBA lo repone a 256)
        foreach (var name in new[] { "rounded", "rounded_grad", "rounded_line" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
            imp.spritePixelsPerUnit = 100f;
            imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 21: píldora HD definitiva ==================
    // Los redondeados pasan a 512x256 con forma de CÁPSULA COMPLETA
    // (radio = mitad de la altura). Al renderizarse las esquinas se
    // REDUCEN desde alta resolución → bordes suaves, sin pixeleo. El
    // 9-slice con bordes a media altura hace que TODO elemento (botón,
    // pestaña, fila) quede píldora perfecta a cualquier tamaño.

    public static void Phase21()
    {
        try { Phase21Work(); Debug.Log("ASTRODASH PHASE21 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE21 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 21 - Píldora HD")]
    public static void Phase21FromMenu()
    {
        Phase21Work();
        Debug.Log("ASTRODASH PHASE21 OK — píldora HD aplicada");
    }

    private static void Phase21Work()
    {
        const int W = 512, H = 256;

        // SDF de cápsula en espacio de PÍXELES (radio = media altura)
        float PillSdf(float x, float y)
        {
            float hw = W / 2f - 2f, hh = H / 2f - 2f, r = hh;
            float qx = Mathf.Abs(x - W / 2f) - (hw - r);
            float qy = Mathf.Abs(y - H / 2f) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        // Relleno plano
        MakeSpriteSized("rounded", W, H, (x, y) =>
            new Color(1f, 1f, 1f, Mathf.Clamp01(-PillSdf(x, y) / 1.5f)));

        // Relleno con gradiente vertical (claro arriba)
        MakeSpriteSized("rounded_grad", W, H, (x, y) =>
        {
            float a = Mathf.Clamp01(-PillSdf(x, y) / 1.5f);
            float b = 0.78f + 0.22f * (y / H);
            return new Color(b, b, b, a);
        });

        // Solo borde (para botones outline)
        MakeSpriteSized("rounded_line", W, H, (x, y) =>
        {
            float s = PillSdf(x, y);
            const float t = 6f;
            float a = Mathf.Clamp01((s + t) / 1.5f) * Mathf.Clamp01(-s / 1.5f);
            return new Color(1f, 1f, 1f, a);
        });

        // Bordes 9-slice: 128 a los lados (la semicircunferencia entera)
        // y 120 arriba/abajo (deja una franja central estirable)
        foreach (var name in new[] { "rounded", "rounded_grad", "rounded_line" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
            imp.spriteBorder = new Vector4(128f, 120f, 128f, 120f);
            imp.spritePixelsPerUnit = 100f;
            imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    /// Como MakeSpriteRGBA pero con tamaño arbitrario y sombreado en
    /// coordenadas de PÍXEL (x derecha, y arriba), supermuestreo 2x2
    private static Sprite MakeSpriteSized(string name, int width, int height,
        System.Func<float, float, Color> shadePx)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color acc = Color.clear;
            for (int sy = 0; sy < 2; sy++)
            for (int sx = 0; sx < 2; sx++)
                acc += shadePx(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
            pixels[y * width + x] = acc / 4f;
        }
        tex.SetPixels(pixels);
        tex.Apply();
        string path = $"{SpritesDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ================= FASE 29: auras sin arcos ========================
    // A petición del usuario: fuera los ARCOS giratorios — el aura queda
    // en halo suave + anillo limpio (con su latido). También reimporta
    // el star_pickup.wav re-exportado con fade-out en el último 25%.

    public static void Phase29()
    {
        try { Phase29Work(); Debug.Log("ASTRODASH PHASE29 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE29 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 29 - Auras sin arcos")]
    public static void Phase29FromMenu()
    {
        Phase29Work();
        Debug.Log("ASTRODASH PHASE29 OK");
    }

    private static void Phase29Work()
    {
        AssetDatabase.ImportAsset("Assets/Audio/star_pickup.wav",
            ImportAssetOptions.ForceUpdate);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = Object.FindFirstObjectByType<PlayerController>().transform;
        foreach (var auraName in new[] { "AuraShield", "AuraMagnet" })
        {
            var aura = player.Find(auraName);
            if (aura == null) throw new System.Exception($"No existe {auraName} (¿falta la Fase 28?)");
            DestroyChild(aura, "Arcs");
        }
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 28: SFX de recogida, auras pro y pulido UI ==
    // 1) Sonidos del usuario (recortados, 1.75x): estrella y power-up
    //    (el de power-up sustituye a los 4 anteriores y al del escudo).
    // 2) Auras rediseñadas: halo suave + ANILLO nítido + ARCOS que giran
    //    en sentido contrario + latido (AuraPulse) — nada de círculo plano.
    // 3) Píldora de estrellas de la TIENDA alineada con la del menú.
    // 4) Botón MENÚ del game over en cápsula real (pill_flat/line 620x120,
    //    la lección: nada de 9-slice en cápsulas) y botones de idioma a
    //    280x84 (tamaño exacto 2x de sus texturas).

    public static void Phase28()
    {
        try { Phase28Work(); Debug.Log("ASTRODASH PHASE28 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE28 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 28 - SFX recogida, auras y pulido")]
    public static void Phase28FromMenu()
    {
        Phase28Work();
        Debug.Log("ASTRODASH PHASE28 OK");
    }

    private static void Phase28Work()
    {
        // ---- 1) SFX de recogida ----
        AssetDatabase.ImportAsset("Assets/Audio/star_pickup.wav");
        AssetDatabase.ImportAsset("Assets/Audio/powerup_pickup.wav");
        var starClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/star_pickup.wav");
        var powerClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/powerup_pickup.wav");
        if (starClip == null || powerClip == null)
            throw new FileNotFoundException("Faltan star_pickup.wav / powerup_pickup.wav");

        // Estrella: en el prefab Item (CollectibleItem.collectClip)
        string itemPath = $"{PrefabsDir}/Item.prefab";
        var itemRoot = PrefabUtility.LoadPrefabContents(itemPath);
        SetField(itemRoot.GetComponent<CollectibleItem>(), "collectClip", starClip);
        PrefabUtility.SaveAsPrefabAsset(itemRoot, itemPath);
        PrefabUtility.UnloadPrefabContents(itemRoot);

        // ---- Texturas de las auras (PPU = ancho → sprite de 1 unidad) ----
        const int A = 512;
        const float cx = A / 2f, cy = A / 2f, rim = 230f;
        float Dist(float x, float y)
            => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

        // Halo relleno suave (más denso en el borde, hueco en el centro)
        MakeSpriteSized("aura_glow", A, A, (x, y) =>
        {
            float d = Dist(x, y) / rim;
            float a = d > 1f ? 0f : Mathf.Pow(d, 2.2f) * 0.5f;
            return new Color(1f, 1f, 1f, a);
        });
        // Anillo nítido de borde suave
        MakeSpriteSized("aura_ring", A, A, (x, y) =>
        {
            float d = Dist(x, y) - rim;
            float a = Mathf.Exp(-(d * d) / (2f * 9f * 9f));
            return new Color(1f, 1f, 1f, a);
        });
        // Tres ARCOS de ~76° sobre el mismo radio (giran aparte)
        MakeSpriteSized("aura_arcs", A, A, (x, y) =>
        {
            float d = Dist(x, y) - rim;
            float radial = Mathf.Exp(-(d * d) / (2f * 14f * 14f));
            float ang = Mathf.Atan2(y - cy, x - cx) * Mathf.Rad2Deg + 180f;
            float local = Mathf.Abs(ang % 120f - 60f);          // 0 en el centro del arco
            float angular = 1f - Mathf.Clamp01((local - 28f) / 10f);
            return new Color(1f, 1f, 1f, radial * angular);
        });
        foreach (var name in new[] { "aura_glow", "aura_ring", "aura_arcs" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
            imp.spritePixelsPerUnit = A;
            imp.SaveAndReimport();
        }
        var glowSpr = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/aura_glow.png");
        var ringSpr = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/aura_ring.png");
        var arcsSpr = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/aura_arcs.png");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Power-up: mismo clip para el escudo y los otros 4 tipos
        var pp = Object.FindFirstObjectByType<PlayerPowerUps>();
        var ppSo = new SerializedObject(pp);
        ppSo.FindProperty("shieldPickupClip").objectReferenceValue = powerClip;
        var arr = ppSo.FindProperty("pickupClips");
        for (int i = 0; i < arr.arraySize; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = powerClip;
        ppSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- 2) Auras nuevas ----
        var player = pp.transform;
        var cyan = new Color(34 / 255f, 211 / 255f, 238 / 255f);
        var violet = new Color(167 / 255f, 139 / 255f, 250 / 255f);
        DestroyChild(player, "AuraShield");
        DestroyChild(player, "AuraMagnet");
        var shield = MakeLayeredAura(player, "AuraShield", glowSpr, ringSpr, arcsSpr,
            cyan, 1.55f, 18f, -50f);
        var magnet = MakeLayeredAura(player, "AuraMagnet", glowSpr, ringSpr, arcsSpr,
            violet, 1.15f, -22f, 65f);
        ppSo.Update();
        ppSo.FindProperty("shieldVisual").objectReferenceValue = shield;
        ppSo.FindProperty("magnetVisual").objectReferenceValue = magnet;
        ppSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- 3) Píldora de estrellas de la tienda = la del menú ----
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var balance = (RectTransform)canvas.transform.Find("ShopPanel/Balance");
        balance.anchorMin = balance.anchorMax = balance.pivot = new Vector2(1f, 1f);
        balance.anchoredPosition = new Vector2(-40f, -55f);
        balance.sizeDelta = new Vector2(230f, 84f);
        var balIcon = (RectTransform)balance.Find("Icon");
        balIcon.anchorMin = balIcon.anchorMax = balIcon.pivot = new Vector2(0f, 0.5f);
        balIcon.anchoredPosition = new Vector2(28f, 0f);
        balIcon.sizeDelta = new Vector2(44f, 44f);
        var balText = balance.Find("StarsText").GetComponent<TextMeshProUGUI>();
        var brt = (RectTransform)balText.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0f, 0.5f);
        brt.anchoredPosition = new Vector2(150f, 0f);
        brt.sizeDelta = new Vector2(140f, 84f);
        balText.fontSize = 40f;
        balText.alignment = TextAlignmentOptions.MidlineLeft;

        // ---- 4) Botón MENÚ en cápsula real + idioma a tamaño exacto ----
        var pillFlat = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_flat.png");
        var pillLine = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_line.png");
        var gameOverPanel = new SerializedObject(hud)
            .FindProperty("gameOverPanel").objectReferenceValue as GameObject;
        var menuBtn = gameOverPanel.transform.Find("MenuButton").GetComponent<Button>();
        var mrt = (RectTransform)menuBtn.transform;
        mrt.sizeDelta = new Vector2(620f, 120f);          // tamaño exacto 2x de la textura
        ((RectTransform)menuBtn.transform.Find("Label")).sizeDelta = new Vector2(620f, 120f);
        menuBtn.image.sprite = pillFlat;
        menuBtn.image.type = Image.Type.Simple;
        menuBtn.image.color = new Color(1f, 1f, 1f, 0.05f);
        DestroyChild(menuBtn.transform, "Border");
        var borderGo = new GameObject("Border", typeof(RectTransform));
        borderGo.transform.SetParent(menuBtn.transform, false);
        var bord = (RectTransform)borderGo.transform;
        bord.anchorMin = Vector2.zero;
        bord.anchorMax = Vector2.one;
        bord.offsetMin = bord.offsetMax = Vector2.zero;
        var bimg = borderGo.AddComponent<Image>();
        bimg.sprite = pillLine;
        bimg.type = Image.Type.Simple;
        bimg.color = new Color(1f, 1f, 1f, 0.16f);
        bimg.raycastTarget = false;

        var settingsPanel = canvas.transform.Find("SettingsPanel");
        ((RectTransform)settingsPanel.Find("SpanishButton")).sizeDelta = new Vector2(280f, 84f);
        ((RectTransform)settingsPanel.Find("EnglishButton")).sizeDelta = new Vector2(280f, 84f);

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    /// Aura de power-up en capas: halo + anillo (gira lento) + arcos
    /// (giran al contrario) + latido en el raíz. Apagada de fábrica.
    private static GameObject MakeLayeredAura(Transform parent, string name,
        Sprite glow, Sprite ring, Sprite arcs, Color tint, float scale,
        float ringSpin, float arcsSpin)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * scale;
        root.AddComponent<AuraPulse>();

        void Layer(string layerName, Sprite sprite, float alpha, float spin)
        {
            var go = new GameObject(layerName);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(tint.r, tint.g, tint.b, alpha);
            sr.sortingOrder = -1;             // detrás de la nave
            if (spin != 0f)
                go.AddComponent<AuraSpin>().degreesPerSecond = spin;
        }
        Layer("Glow", glow, 0.35f, 0f);
        Layer("Ring", ring, 0.85f, ringSpin);
        Layer("Arcs", arcs, 0.95f, arcsSpin);

        root.SetActive(false);                // las enciende PlayerPowerUps
        return root;
    }

    // ================= FASE 27: idioma ES/EN, menú en game over y llama ==
    // 1) Localización: LocalizedText (clave → Loc.cs) en cada texto fijo
    //    de la UI + selector ESPAÑOL|ENGLISH en configuración.
    // 2) Botón MENÚ en la pantalla de game over (volver sin reintentar).
    // 3) Llama del motor profesional: cono cerrado, partículas ESTIRADAS
    //    por velocidad, núcleo claro → naranja → transparente.

    public static void Phase27()
    {
        try { Phase27Work(); Debug.Log("ASTRODASH PHASE27 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE27 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 27 - Idioma, menú en game over y llama")]
    public static void Phase27FromMenu()
    {
        Phase27Work();
        Debug.Log("ASTRODASH PHASE27 OK");
    }

    private static void Phase27Work()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var hudSo = new SerializedObject(hud);
        var startPanel = hudSo.FindProperty("startPanel").objectReferenceValue as GameObject;
        var gameOverPanel = hudSo.FindProperty("gameOverPanel").objectReferenceValue as GameObject;
        var settingsPanel = canvas.transform.Find("SettingsPanel").gameObject;
        var shopPanel = canvas.transform.Find("ShopPanel").gameObject;
        var pausePanel = canvas.transform.Find("PausePanel").gameObject;

        // ---- 1a) Claves de localización en los textos fijos ----
        Localize(startPanel.transform, "PlayButton/Label", "play");
        Localize(startPanel.transform, "ShopButton/Label", "shop");
        Localize(startPanel.transform, "SettingsButton/Label", "settings");
        Localize(startPanel.transform, "HintText", "hint");
        Localize(settingsPanel.transform, "TitleText", "settings");
        Localize(settingsPanel.transform, "MasterGroup/Label", "master_volume");
        Localize(settingsPanel.transform, "MusicGroup/Label", "music");
        Localize(settingsPanel.transform, "SfxGroup/Label", "sfx");
        Localize(settingsPanel.transform, "BackButton/Label", "back");
        Localize(shopPanel.transform, "TitleText", "shop");
        Localize(shopPanel.transform, "TabShips/Label", "ships");
        Localize(shopPanel.transform, "TabShots/Label", "shots");
        Localize(shopPanel.transform, "TabBackgrounds/Label", "backgrounds");
        Localize(shopPanel.transform, "CloseButton/Label", "back");
        Localize(pausePanel.transform, "TitleText", "paused");
        Localize(pausePanel.transform, "ResumeButton/Label", "resume");
        Localize(pausePanel.transform, "QuitButton/Label", "quit_menu");
        Localize(gameOverPanel.transform, "TitleText", "game_over");
        Localize(gameOverPanel.transform, "RetryText", "tap_retry");

        var sg = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/SpaceGrotesk SDF.asset");
        var rounded = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded.png");
        var line = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/rounded_line.png");
        var pillGrad = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_btn_grad.png");
        var pillFlat = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_btn_flat.png");

        // ---- 1b) Fila IDIOMA en configuración: etiqueta + 2 cápsulas ----
        DestroyChild(settingsPanel.transform, "LanguageLabel");
        DestroyChild(settingsPanel.transform, "SpanishButton");
        DestroyChild(settingsPanel.transform, "EnglishButton");
        var langLabel = MakeText(settingsPanel.transform, "LanguageLabel", "IDIOMA",
            36, DsNeutral, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -360f), new Vector2(700f, 60f));
        if (sg != null) langLabel.font = sg;
        langLabel.gameObject.AddComponent<LocalizedText>();
        SetStringField(langLabel.GetComponent<LocalizedText>(), "key", "language");

        var esBtn = MakeButton(settingsPanel.transform, "SpanishButton", "ESPAÑOL",
            Color.clear, DsNeutral, new Vector2(-165f, -460f), new Vector2(300f, 88f), 34f);
        var enBtn = MakeButton(settingsPanel.transform, "EnglishButton", "ENGLISH",
            Color.clear, DsNeutral, new Vector2(165f, -460f), new Vector2(300f, 88f), 34f);
        if (sg != null)
        {
            esBtn.GetComponentInChildren<TextMeshProUGUI>().font = sg;
            enBtn.GetComponentInChildren<TextMeshProUGUI>().font = sg;
        }

        var oldSel = settingsPanel.GetComponent<LanguageSelector>();
        if (oldSel != null) Object.DestroyImmediate(oldSel);
        var selector = settingsPanel.AddComponent<LanguageSelector>();
        var selSo = new SerializedObject(selector);
        selSo.FindProperty("spanishButton").objectReferenceValue = esBtn;
        selSo.FindProperty("englishButton").objectReferenceValue = enBtn;
        selSo.FindProperty("activePill").objectReferenceValue = pillGrad;
        selSo.FindProperty("inactivePill").objectReferenceValue = pillFlat;
        selSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- 2) Botón MENÚ del game over (bajo "toca para reintentar") ----
        DestroyChild(gameOverPanel.transform, "MenuButton");
        var menuBtn = MakeButton(gameOverPanel.transform, "MenuButton", "MENÚ",
            Color.clear, DsNeutral, new Vector2(0f, -790f), new Vector2(480f, 100f), 40f);
        StyleOutlineButton(menuBtn, rounded, line, DsNeutral);
        if (sg != null) menuBtn.GetComponentInChildren<TextMeshProUGUI>().font = sg;
        var menuLbl = menuBtn.GetComponentInChildren<TextMeshProUGUI>();
        menuLbl.gameObject.AddComponent<LocalizedText>();
        SetStringField(menuLbl.GetComponent<LocalizedText>(), "key", "menu");
        hudSo.FindProperty("gameOverMenuButton").objectReferenceValue = menuBtn;
        hudSo.ApplyModifiedPropertiesWithoutUndo();

        // ---- 3) Llama del motor ----
        var player = Object.FindFirstObjectByType<PlayerController>();
        var fire = player.transform.Find("EngineFire").GetComponent<ParticleSystem>();
        var main = fire.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.10f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.96f, 0.75f),      // núcleo casi blanco
            new Color(0.99f, 0.80f, 0.35f));  // amarillo cálido

        var emission = fire.emission;
        emission.rateOverTime = 75f;

        var shape = fire.shape;
        shape.angle = 7f;                     // cono cerrado: chorro, no nube
        shape.radius = 0.025f;

        // Núcleo claro → naranja → rojo apagado, desvaneciéndose
        var col = fire.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.98f, 0.90f), 0f),
                new GradientColorKey(new Color(1f, 0.62f, 0.20f), 0.45f),
                new GradientColorKey(new Color(0.85f, 0.25f, 0.10f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.55f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var size = fire.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

        // Estiradas según su velocidad: lenguas de fuego, no circulitos
        var renderer = fire.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1.6f;

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    /// Añade LocalizedText con su clave al texto en `path` bajo `root`
    private static void Localize(Transform root, string path, string key)
    {
        var t = root.Find(path);
        if (t == null) throw new System.Exception($"Localize: no existe '{path}' bajo {root.name}");
        var lt = t.GetComponent<LocalizedText>();
        if (lt == null) lt = t.gameObject.AddComponent<LocalizedText>();
        SetStringField(lt, "key", key);
    }

    /// Asigna un campo string [SerializeField] privado (SetField es solo
    /// para referencias a objetos)
    private static void SetStringField(Object target, string fieldName, string value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).stringValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ================= FASE 26: música propia de la partida ============
    // Pista nueva del usuario para el gameplay (OGG extraído de su
    // MP4/AAC); la de siempre queda para menú/tienda/ajustes. El cambio
    // de pista lo hace GameManager.SetState según el estado.

    public static void Phase26()
    {
        try { Phase26Work(); Debug.Log("ASTRODASH PHASE26 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE26 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 26 - Música de la partida")]
    public static void Phase26FromMenu()
    {
        Phase26Work();
        Debug.Log("ASTRODASH PHASE26 OK");
    }

    private static void Phase26Work()
    {
        const string clipPath = "Assets/Audio/gameplay_music.ogg";
        AssetDatabase.ImportAsset(clipPath);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (clip == null) throw new FileNotFoundException("No se pudo cargar " + clipPath);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SetField(Object.FindFirstObjectByType<AudioManager>(), "gameplayMusicClip", clip);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 25: cierre pre-build Android ===============
    // Los tres flecos antes del primer build en teléfono: sonido de
    // explosión/game over (mismo clip del usuario, la explosión ES el
    // game over), icono de la app (logo del usuario) y package name.

    public static void Phase25()
    {
        try { Phase25Work(); Debug.Log("ASTRODASH PHASE25 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE25 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 25 - Cierre pre-build Android")]
    public static void Phase25FromMenu()
    {
        Phase25Work();
        Debug.Log("ASTRODASH PHASE25 OK");
    }

    private static void Phase25Work()
    {
        // 1) SFX de game over (solo la muerte del jugador suena; WAV —
        // el "mp3" original era un contenedor MP4/AAC que Unity no decodifica)
        const string clipPath = "Assets/Audio/explosion_gameover.wav";
        AssetDatabase.ImportAsset(clipPath);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        if (clip == null) throw new FileNotFoundException("No se pudo cargar " + clipPath);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SetField(Object.FindFirstObjectByType<GameManager>(), "gameOverClip", clip);
        EditorSceneManager.SaveScene(scene);

        // 2) Icono de la app (default para todas las plataformas)
        const string iconPath = "Assets/Icons/app_icon.png";
        AssetDatabase.ImportAsset(iconPath);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        if (icon == null) throw new FileNotFoundException("No se pudo cargar " + iconPath);
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown,
                                new[] { icon }, IconKind.Any);

        // 3) Identidad Android para el build y la Play Store
        PlayerSettings.SetApplicationIdentifier(
            UnityEditor.Build.NamedBuildTarget.Android, "com.Yaken.astrodash");

        AssetDatabase.SaveAssets();
    }

    // ================= FASE 24: fix explosión en bucle =================
    // El prefab MeteorExplosion tenía loop=true (default de Unity): cada
    // estallido se REPETÍA cada 5 s en su posición vieja y nunca volvía
    // al pool — las "partículas de la nada" del usuario. Un solo burst.

    public static void Phase24()
    {
        try { Phase24Work(); Debug.Log("ASTRODASH PHASE24 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE24 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 24 - Fix explosión en bucle")]
    public static void Phase24FromMenu()
    {
        Phase24Work();
        Debug.Log("ASTRODASH PHASE24 OK");
    }

    private static void Phase24Work()
    {
        string path = $"{PrefabsDir}/MeteorExplosion.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        var main = root.GetComponent<ParticleSystem>().main;
        main.loop = false;      // un solo estallido y de vuelta al pool
        main.duration = 0.6f;   // ventana justa para el burst + vidas
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 23: píldoras en la tienda ==================
    // Cápsulas dedicadas (2x) para pestañas y botones de fila de la
    // tienda — cada tamaño con su textura exacta para que los extremos
    // sean semicírculos perfectos. Paneles opacos (el menú se
    // transparentaba detrás).

    public static void Phase23()
    {
        try { Phase23Work(); Debug.Log("ASTRODASH PHASE23 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE23 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 23 - Píldoras en tienda")]
    public static void Phase23FromMenu()
    {
        Phase23Work();
        Debug.Log("ASTRODASH PHASE23 OK");
    }

    private static void Phase23Work()
    {
        // Cápsulas 2x: pestañas (330x90) y botones de fila (280x84)
        MakePillTexture("pill_tab_grad", 660, 180, true);
        MakePillTexture("pill_btn_grad", 560, 168, true);
        MakePillTexture("pill_btn_flat", 560, 168, false);

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;

        // Paneles opacos, como las tarjetas del mockup
        var opaque = new Color(DsPanel.r, DsPanel.g, DsPanel.b, 1f);
        foreach (var panelName in new[] { "SettingsPanel", "ShopPanel", "PausePanel" })
            canvas.transform.Find(panelName).GetComponent<Image>().color = opaque;

        // Cablear las cápsulas nuevas en la tienda
        var shop = canvas.GetComponent<ShopController>();
        var so = new SerializedObject(shop);
        so.FindProperty("tabPillSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_tab_grad.png");
        so.FindProperty("buttonPillSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_btn_grad.png");
        so.FindProperty("buttonPillFlatSprite").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_btn_flat.png");
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    /// Cápsula (radio = media altura) en un lienzo w×h, plana o con
    /// gradiente vertical, lista para Image.Type.Simple
    private static void MakePillTexture(string name, int w, int h, bool gradient)
    {
        float Sdf(float x, float y)
        {
            float hw = w / 2f - 2f, hh = h / 2f - 2f, r = hh;
            float qx = Mathf.Abs(x - w / 2f) - (hw - r);
            float qy = Mathf.Abs(y - h / 2f) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        MakeSpriteSized(name, w, h, (x, y) =>
        {
            float a = Mathf.Clamp01(-Sdf(x, y) / 2f);
            float b = gradient ? 0.78f + 0.22f * (y / h) : 1f;
            return new Color(b, b, b, a);
        });
    }

    // ================= FASE 22: píldoras reales ========================
    // El 9-slice deforma las cápsulas (Unity encoge los bordes por eje
    // independiente). Solución definitiva:
    //  - Botones GRANDES (menú/volver/pausa): textura cápsula 1240x240
    //    (2x) con Image.Type.SIMPLE — semicírculos perfectos y bordes
    //    suaves por reducción.
    //  - Elementos pequeños (tabs, filas, sliders, acciones de tienda):
    //    vuelven al radio 40 con 9-slice, que ahí sí funciona bien.

    public static void Phase22()
    {
        try { Phase22Work(); Debug.Log("ASTRODASH PHASE22 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE22 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 22 - Píldoras reales")]
    public static void Phase22FromMenu()
    {
        Phase22Work();
        Debug.Log("ASTRODASH PHASE22 OK");
    }

    private static void Phase22Work()
    {
        // --- 1) Redondeados chicos: radio 40, 9-slice (como la Fase 19) ---
        MakeSpriteRGBA("rounded", ShadeRoundedPanel);
        SetBorder("rounded", 40f);
        MakeSpriteRGBA("rounded_grad", ShadeRoundedGrad);
        SetBorder("rounded_grad", 40f);
        MakeSpriteRGBA("rounded_line", ShadeRoundedLine);
        SetBorder("rounded_line", 40f);
        foreach (var name in new[] { "rounded", "rounded_grad", "rounded_line" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
            imp.spritePixelsPerUnit = 100f;
            imp.SaveAndReimport();
        }

        // --- 2) Cápsulas HD para los botones grandes (sin 9-slice) ---
        const int W = 1240, H = 240;   // 2x de un botón de 620x120
        float PillSdf(float x, float y)
        {
            float hw = W / 2f - 2f, hh = H / 2f - 2f, r = hh;
            float qx = Mathf.Abs(x - W / 2f) - (hw - r);
            float qy = Mathf.Abs(y - H / 2f) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
        MakeSpriteSized("pill_flat", W, H, (x, y) =>
            new Color(1f, 1f, 1f, Mathf.Clamp01(-PillSdf(x, y) / 2f)));
        MakeSpriteSized("pill_grad", W, H, (x, y) =>
        {
            float a = Mathf.Clamp01(-PillSdf(x, y) / 2f);
            float b = 0.78f + 0.22f * (y / H);
            return new Color(b, b, b, a);
        });
        MakeSpriteSized("pill_line", W, H, (x, y) =>
        {
            float s = PillSdf(x, y);
            const float t = 8f;
            float a = Mathf.Clamp01((s + t) / 2f) * Mathf.Clamp01(-s / 2f);
            return new Color(1f, 1f, 1f, a);
        });

        var pillFlat = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_flat.png");
        var pillGrad = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_grad.png");
        var pillLine = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpritesDir}/pill_line.png");

        // --- 3) Escena: cápsulas en los botones grandes ---
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var canvas = hud.gameObject;
        var startPanel = new SerializedObject(hud)
            .FindProperty("startPanel").objectReferenceValue as GameObject;
        var settingsPanel = canvas.transform.Find("SettingsPanel");
        var shopPanel = canvas.transform.Find("ShopPanel");
        var pausePanel = canvas.transform.Find("PausePanel");

        void PillGradBtn(Button b, Color tint, Color textColor)
        {
            b.image.sprite = pillGrad;
            b.image.type = Image.Type.Simple;
            b.image.color = tint;
            DestroyChild(b.transform, "Border");
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = textColor;
        }
        void PillOutlineBtn(Button b, Color textColor)
        {
            b.image.sprite = pillFlat;
            b.image.type = Image.Type.Simple;
            b.image.color = new Color(1f, 1f, 1f, 0.05f);
            DestroyChild(b.transform, "Border");
            var borderGo = new GameObject("Border", typeof(RectTransform));
            borderGo.transform.SetParent(b.transform, false);
            var brt = (RectTransform)borderGo.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            var bimg = borderGo.AddComponent<Image>();
            bimg.sprite = pillLine;
            bimg.type = Image.Type.Simple;
            bimg.color = new Color(1f, 1f, 1f, 0.16f);
            bimg.raycastTarget = false;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.color = textColor;
        }

        PillGradBtn(startPanel.transform.Find("PlayButton").GetComponent<Button>(), DsCyan, DsCyanText);
        PillGradBtn(startPanel.transform.Find("ShopButton").GetComponent<Button>(), DsGold, DsGoldText);
        PillOutlineBtn(startPanel.transform.Find("SettingsButton").GetComponent<Button>(), DsNeutral);
        PillGradBtn(settingsPanel.Find("BackButton").GetComponent<Button>(), DsCyan, DsCyanText);
        PillOutlineBtn(shopPanel.Find("CloseButton").GetComponent<Button>(), DsNeutral);
        PillGradBtn(pausePanel.Find("ResumeButton").GetComponent<Button>(), DsCyan, DsCyanText);
        PillOutlineBtn(pausePanel.Find("QuitButton").GetComponent<Button>(), DsNeutral);

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= FASE 20: modo píldora (opcional) ================
    // Radio 55 px: los botones del menú (110-120 de alto) quedan como
    // cápsulas con extremos semicirculares. Ejecutable desde el menú del
    // editor para ver el cambio en vivo. Para volver al radio 40, correr
    // "Fase 19 - Radio 40".

    private static float roundedRadius = 0.15625f;   // 40/256 por defecto

    public static void Phase20()
    {
        try { Phase20Work(); Debug.Log("ASTRODASH PHASE20 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE20 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 20 - Modo píldora")]
    public static void Phase20FromMenu()
    {
        Phase20Work();
        Debug.Log("ASTRODASH PHASE20 OK — botones en modo píldora");
    }

    private static void Phase20Work()
    {
        roundedRadius = 0.215f;   // 55/256: cápsula en los botones del menú
        try
        {
            MakeSpriteRGBA("rounded", ShadeRoundedPanel);
            SetBorder("rounded", 55f);
            MakeSpriteRGBA("rounded_grad", ShadeRoundedGrad);
            SetBorder("rounded_grad", 55f);
            MakeSpriteRGBA("rounded_line", ShadeRoundedLine);
            SetBorder("rounded_line", 55f);
            foreach (var name in new[] { "rounded", "rounded_grad", "rounded_line" })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath($"{SpritesDir}/{name}.png");
                imp.spritePixelsPerUnit = 100f;
                imp.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            roundedRadius = 0.15625f;
        }
    }

    // ================= FASE 18: estado inicial saneado ==================
    // La escena se guardaba con el marcador, el botón de pausa y la nave
    // VISIBLES (a runtime los oculta cada Start()) — si cualquier Start
    // falla, el menú aparece contaminado con la UI de partida (el bug de
    // la captura del usuario). Se guardan apagados de fábrica: los
    // scripts los ENCIENDEN cuando toca, en vez de tener que apagarlos.

    public static void Phase18()
    {
        try { Phase18Work(); Debug.Log("ASTRODASH PHASE18 OK"); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError("ASTRODASH PHASE18 FAILED: " + e); EditorApplication.Exit(1); }
    }

    [MenuItem("AstroDash/Fase 18 - Estado inicial saneado")]
    public static void Phase18FromMenu()
    {
        Phase18Work();
        Debug.Log("ASTRODASH PHASE18 OK");
    }

    private static void Phase18Work()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = Object.FindFirstObjectByType<HUDController>();
        var hudSo = new SerializedObject(hud);

        // Marcador y línea de power-ups: apagados (solo se ven jugando)
        (hudSo.FindProperty("scoreText").objectReferenceValue as TextMeshProUGUI)
            .gameObject.SetActive(false);
        var powerUpText = hudSo.FindProperty("powerUpText").objectReferenceValue as TextMeshProUGUI;
        if (powerUpText != null) powerUpText.text = "";

        // Botón de pausa: apagado (PauseController lo enciende al jugar)
        var pause = Object.FindFirstObjectByType<PauseController>();
        var pauseBtn = new SerializedObject(pause)
            .FindProperty("pauseButton").objectReferenceValue as Button;
        pauseBtn.gameObject.SetActive(false);

        // Nave: sprite apagado (PlayerController lo enciende al jugar)
        var player = Object.FindFirstObjectByType<PlayerController>();
        player.GetComponent<SpriteRenderer>().enabled = false;

        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    // ================= DIAGNÓSTICO: play mode en batch ==================
    // Entra en Play, comprueba el estado del menú, pulsa JUGAR por código
    // y verifica que el panel se oculte y la nave entre. Captura cualquier
    // excepción de runtime. Resultado en el log como "ASTRODASH DIAG".

    private static System.Text.StringBuilder diagLog;
    private static int diagFrames;      // ticks del EDITOR (solo como tope)
    private static int diagStage;       // etapas por FRAMES DE JUEGO

    public static void Diag()
    {
        diagLog = new System.Text.StringBuilder();
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Exception || type == LogType.Error)
                diagLog.AppendLine($"[RUNTIME {type}] {msg}\n{stack}");
        };
        EditorApplication.playModeStateChanged += change =>
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                diagFrames = 0;
                diagStage = 0;
                EditorApplication.update += DiagUpdate;
            }
        };
        // Sin esto, el play mode arranca en una escena VACÍA (el batch
        // no abre ninguna escena por defecto)
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void DiagUpdate()
    {
        diagFrames++;
        try
        {
            // Las etapas van por FRAMES DE JUEGO (Time.frameCount): en
            // batch los ticks del editor y los frames del juego no van 1:1
            int f = Time.frameCount;

            if (diagStage == 0 && f >= 40)
            {
                diagStage = 1;
                Snapshot("MENÚ");
                CaptureMenuPng();
                // Abrir la TIENDA para capturarla también
                var shopCtl = Object.FindFirstObjectByType<ShopController>();
                (new SerializedObject(shopCtl).FindProperty("shopButton")
                    .objectReferenceValue as Button).onClick.Invoke();
            }
            else if (diagStage == 1 && f >= 100)
            {
                diagStage = 2;
                CapturePngAs("diag_shop.png");
                var shopCtl = Object.FindFirstObjectByType<ShopController>();
                (new SerializedObject(shopCtl).FindProperty("closeButton")
                    .objectReferenceValue as Button).onClick.Invoke();
                var hud = Object.FindFirstObjectByType<HUDController>();
                var startPanel = new SerializedObject(hud)
                    .FindProperty("startPanel").objectReferenceValue as GameObject;
                var play = startPanel.transform.Find("PlayButton").GetComponent<Button>();
                diagLog.AppendLine("Pulsando JUGAR...");
                play.onClick.Invoke();
            }
            else if (diagStage == 2 && f >= 300)
            {
                diagStage = 3;
                Snapshot("TRAS BOTÓN (~200 frames después)");
                // Si el botón/corrutina no arrancó la partida, probar la
                // vía directa para acotar dónde está el fallo
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Start)
                {
                    diagLog.AppendLine("El botón NO arrancó: llamando StartGame() directo...");
                    GameManager.Instance.StartGame();
                }
            }
            else if (diagStage == 3 && f >= 420)
            {
                Snapshot("FINAL");
                Debug.Log("ASTRODASH DIAG:\n" + diagLog);
                EditorApplication.update -= DiagUpdate;
                EditorApplication.Exit(0);
            }
            else if (diagFrames > 200000)   // tope de seguridad
            {
                diagLog.AppendLine($"TIMEOUT: ticks editor={diagFrames}, frames juego={f}");
                Debug.Log("ASTRODASH DIAG:\n" + diagLog);
                EditorApplication.update -= DiagUpdate;
                EditorApplication.Exit(1);
            }
        }
        catch (System.Exception e)
        {
            diagLog.AppendLine("DIAG EXCEPTION: " + e);
            Debug.Log("ASTRODASH DIAG:\n" + diagLog);
            EditorApplication.update -= DiagUpdate;
            EditorApplication.Exit(1);
        }
    }

    private static void CaptureMenuPng() => CapturePngAs("diag_menu.png");

    /// Renderiza el Canvas a un PNG (1080x1920) tal y como lo pinta el
    /// juego — para poder VER la UI real desde el modo batch.
    private static void CapturePngAs(string fileName)
    {
        var cam = Camera.main;
        var canvas = Object.FindFirstObjectByType<Canvas>();
        var prevMode = canvas.renderMode;
        var rt = new RenderTexture(1080, 1920, 24);
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
            tex.Apply();
            string path = Path.GetFullPath(Application.dataPath + "/../../" + fileName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            diagLog.AppendLine("Captura guardada: " + path);
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = null;
            canvas.renderMode = prevMode;
            rt.Release();
        }
    }

    private static void Snapshot(string title)
    {
        diagLog.AppendLine($"--- {title} ---");
        diagLog.AppendLine($"frameJuego={Time.frameCount}, unscaledTime={Time.unscaledTime:0.00}s, ticksEditor={diagFrames}");
        var gm = GameManager.Instance;
        diagLog.AppendLine("GameManager.Instance: " + (gm != null) +
                           (gm != null ? " | Estado: " + gm.State : ""));
        var player = Object.FindFirstObjectByType<PlayerController>();
        if (player != null)
            diagLog.AppendLine($"Nave: visible={player.GetComponent<SpriteRenderer>().enabled}, y={player.transform.position.y:0.00}");
        var hud = Object.FindFirstObjectByType<HUDController>();
        var so = new SerializedObject(hud);
        diagLog.AppendLine("StartPanel activo: " +
            (so.FindProperty("startPanel").objectReferenceValue as GameObject).activeSelf);
        diagLog.AppendLine("ScoreText activo: " +
            (so.FindProperty("scoreText").objectReferenceValue as TextMeshProUGUI).gameObject.activeSelf);
        var pause = Object.FindFirstObjectByType<PauseController>();
        if (pause != null)
        {
            var pb = new SerializedObject(pause).FindProperty("pauseButton").objectReferenceValue as Button;
            diagLog.AppendLine("Botón pausa activo: " + pb.gameObject.activeSelf);
        }
        var canvas = hud.GetComponentInParent<Canvas>();
        diagLog.AppendLine("MenuControllers en Canvas: " + canvas.GetComponents<MenuController>().Length);
    }

    // ================= VALIDACIÓN: comprobar el montaje =================
    // Abre la escena generada y verifica que todos los objetos existan y
    // que ninguna referencia del Inspector haya quedado sin asignar.
    public static void Validate()   // entrada para batch: cierra el proceso al acabar
    {
        try
        {
            var result = ValidateWork();
            if (result.Count == 0) { Debug.Log("ASTRODASH VALIDATE OK"); EditorApplication.Exit(0); }
            else { Debug.LogError("ASTRODASH VALIDATE FAILED:\n - " + string.Join("\n - ", result)); EditorApplication.Exit(1); }
        }
        catch (System.Exception e)
        {
            Debug.LogError("ASTRODASH VALIDATE FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    /// La misma validación desde el menú del editor, sin cerrar Unity.
    /// El resultado aparece en la ventana Console.
    [MenuItem("AstroDash/Validar montaje")]
    public static void ValidateFromMenu()
    {
        var result = ValidateWork();
        if (result.Count == 0) Debug.Log("ASTRODASH VALIDATE OK");
        else Debug.LogError("ASTRODASH VALIDATE FAILED:\n - " + string.Join("\n - ", result));
    }

    private static System.Collections.Generic.List<string> ValidateWork()
    {
        {
            var errors = new System.Collections.Generic.List<string>();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var gm = Object.FindFirstObjectByType<GameManager>();
            if (gm == null) errors.Add("Falta GameManager en la escena");
            else if (gm.Config == null) errors.Add("GameManager.config sin asignar");

            var spawner = Object.FindFirstObjectByType<Spawner>();
            if (spawner == null) errors.Add("Falta Spawner");
            else
            {
                CheckRef(spawner, "meteorPool", errors);
                CheckRef(spawner, "itemPool", errors);
            }

            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) errors.Add("Falta PlayerController");
            var shooter = Object.FindFirstObjectByType<Shooter>();
            if (shooter == null) errors.Add("Falta Shooter");
            else
            {
                CheckRef(shooter, "bulletPool", errors);
                CheckRef(shooter, "muzzle", errors);
            }

            var hud = Object.FindFirstObjectByType<HUDController>();
            if (hud == null) errors.Add("Falta HUDController");
            else
                foreach (var f in new[] { "scoreText", "startPanel", "startBestText",
                                          "gameOverPanel", "finalScoreText", "gameOverBestText" })
                    CheckRef(hud, f, errors);

            var starfield = Object.FindFirstObjectByType<Starfield>();
            if (starfield == null) errors.Add("Falta Starfield");
            else CheckRef(starfield, "starPrefab", errors);

            foreach (var pool in Object.FindObjectsByType<SimplePool>(FindObjectsSortMode.None))
                CheckRef(pool, "prefab", errors);

            // Power-ups (Fase 3)
            var powerUpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/PowerUp.prefab");
            if (powerUpPrefab == null) errors.Add("Falta prefab PowerUp");
            else
            {
                // (la letra TMP se eliminó en la Fase 6: el icono identifica
                //  el tipo; typeSprites se comprueba más abajo)
                if (powerUpPrefab.GetComponent<PowerUpItem>() == null)
                    errors.Add("PowerUp: falta componente PowerUpItem");
            }
            if (spawner != null) CheckRef(spawner, "powerUpPool", errors);

            var playerPowerUps = Object.FindFirstObjectByType<PlayerPowerUps>();
            if (playerPowerUps == null) errors.Add("Falta PlayerPowerUps en el Player");
            else
            {
                CheckRef(playerPowerUps, "shieldVisual", errors);
                CheckRef(playerPowerUps, "magnetVisual", errors);
            }

            // VFX (Fase 7)
            var fxManager = Object.FindFirstObjectByType<FxManager>();
            if (fxManager == null) errors.Add("Falta FxManager en la escena");
            else CheckRef(fxManager, "explosionPool", errors);
            if (player != null && player.transform.Find("EngineFire") == null)
                errors.Add("Falta EngineFire en el Player");
            else if (player != null &&
                     player.transform.Find("EngineFire").GetComponent<EngineFireController>() == null)
                errors.Add("EngineFire sin EngineFireController");
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/MeteorExplosion.prefab") == null)
                errors.Add("Falta el prefab MeteorExplosion");

            // Rocas rotas y audio (Fase 8)
            var meteorForBroken = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/Meteor.prefab");
            if (meteorForBroken != null)
            {
                var brokenProp = new SerializedObject(meteorForBroken.GetComponent<Meteor>())
                    .FindProperty("bigRedBrokenSprites");
                if (brokenProp == null || brokenProp.arraySize != 4 ||
                    brokenProp.GetArrayElementAtIndex(0).objectReferenceValue == null)
                    errors.Add("Meteor.bigRedBrokenSprites sin las 4 rocas rotas (¿falta la Fase 8?)");
            }
            var audioManager = Object.FindFirstObjectByType<AudioManager>();
            if (audioManager == null) errors.Add("Falta AudioManager en la escena");
            else
            {
                CheckRef(audioManager, "musicSource", errors);
                CheckRef(audioManager, "sfxSource", errors);
                CheckRef(audioManager, "gameplayMusicClip", errors);  // Fase 26
            }

            // Sonido de disparo (Fase 10)
            if (shooter != null) CheckRef(shooter, "shotClip", errors);

            // Sonido de game over e identidad Android (Fase 25)
            CheckRef(Object.FindFirstObjectByType<GameManager>(), "gameOverClip", errors);

            // Idioma, botón MENÚ del game over y llama (Fase 27)
            CheckRef(Object.FindFirstObjectByType<HUDController>(), "gameOverMenuButton", errors);
            var langSel = Object.FindFirstObjectByType<LanguageSelector>(FindObjectsInactive.Include);
            if (langSel == null) errors.Add("Falta LanguageSelector en SettingsPanel (¿falta la Fase 27?)");
            else
                foreach (var f in new[] { "spanishButton", "englishButton", "activePill", "inactivePill" })
                    CheckRef(langSel, f, errors);
            int locCount = Object.FindObjectsByType<LocalizedText>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (locCount < 19)
                errors.Add($"Solo hay {locCount} LocalizedText en la escena (se esperan 21+)");

            // SFX de recogida y auras en capas (Fase 28)
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/Item.prefab");
            if (itemPrefab != null)
                CheckRef(itemPrefab.GetComponent<CollectibleItem>(), "collectClip", errors);
            var playerTf = Object.FindFirstObjectByType<PlayerController>()?.transform;
            foreach (var auraName in new[] { "AuraShield", "AuraMagnet" })
            {
                // Halo + anillo (los arcos se quitaron en la Fase 29)
                var aura = playerTf != null ? playerTf.Find(auraName) : null;
                if (aura == null || aura.GetComponent<AuraPulse>() == null || aura.childCount != 2)
                    errors.Add($"{auraName} sin estructura de capas (¿faltan las Fases 28/29?)");
            }
            if (PlayerSettings.GetApplicationIdentifier(
                    UnityEditor.Build.NamedBuildTarget.Android) != "com.Yaken.astrodash")
                errors.Add("Package name de Android sin configurar (¿falta la Fase 25?)");

            // Menú, configuración y halo de power-ups (Fases 11, 12 y 15)
            var menuCtrl = Object.FindFirstObjectByType<MenuController>();
            if (menuCtrl == null) errors.Add("Falta MenuController en el Canvas");
            else
                foreach (var f in new[] { "settingsPanel", "playButton", "settingsButton",
                                          "backButton", "masterSlider", "masterValueText",
                                          "musicSlider", "musicValueText", "sfxSlider",
                                          "sfxValueText", "sfxPreviewClip", "menuStarsText",
                                          "startPanelGroup", "titleShipIcon" })
                    CheckRef(menuCtrl, f, errors);

            // Sonidos de recogida (Fase 12)
            if (playerPowerUps != null)
            {
                CheckRef(playerPowerUps, "shieldPickupClip", errors);
                var pickPr = new SerializedObject(playerPowerUps).FindProperty("pickupClips");
                if (pickPr == null || pickPr.arraySize != 4 ||
                    pickPr.GetArrayElementAtIndex(0).objectReferenceValue == null)
                    errors.Add("PlayerPowerUps.pickupClips sin los 4 clips (¿falta la Fase 12?)");
            }

            // Economía y tienda (Fase 13)
            var catalog = Object.FindFirstObjectByType<Catalog>();
            if (catalog == null) errors.Add("Falta Catalog en la escena");
            else
            {
                if (catalog.ships == null || catalog.ships.Length != 6 ||
                    catalog.ships[5].sprite == null)
                    errors.Add("Catalog.ships sin las 6 naves (¿falta la Fase 13?)");
                if (catalog.shots == null || catalog.shots.Length != 6)
                    errors.Add("Catalog.shots sin los 6 disparos");
                if (catalog.backgrounds == null || catalog.backgrounds.Length != 3)
                    errors.Add("Catalog.backgrounds sin los 3 fondos");
            }
            var shopCtrl = Object.FindFirstObjectByType<ShopController>();
            if (shopCtrl == null) errors.Add("Falta ShopController en el Canvas");
            else
                foreach (var f in new[] { "shopPanel", "shopButton", "closeButton",
                                          "shipsTabButton", "shotsTabButton", "backgroundsTabButton",
                                          "content", "starsText", "starIcon", "circleIcon",
                                          "bulletIcon", "roundedSprite", "gradientSprite" })
                    CheckRef(shopCtrl, f, errors);

            // Pausa (Fase 14)
            var pauseCtrl = Object.FindFirstObjectByType<PauseController>();
            if (pauseCtrl == null) errors.Add("Falta PauseController en el Canvas");
            else
                foreach (var f in new[] { "pausePanel", "pauseButton", "resumeButton", "quitButton" })
                    CheckRef(pauseCtrl, f, errors);
            var hudCheck = Object.FindFirstObjectByType<HUDController>();
            if (hudCheck != null)
                foreach (var f in new[] { "starsGroup", "starsText", "gameOverStarsText" })
                    CheckRef(hudCheck, f, errors);
            var puForGlow = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/PowerUp.prefab");
            if (puForGlow != null)
                CheckRef(puForGlow.GetComponent<PowerUpItem>(), "glow", errors);
            if (hud != null) CheckRef(hud, "powerUpText", errors);

            // Prefabs: componentes y físicas correctas
            foreach (var name in new[] { "Bullet", "Meteor", "Item", "PowerUp" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/{name}.prefab");
                if (prefab == null) { errors.Add($"Falta prefab {name}"); continue; }
                var col = prefab.GetComponent<Collider2D>();
                if (col == null || !col.isTrigger) errors.Add($"{name}: collider ausente o sin Is Trigger");
                var rb = prefab.GetComponent<Rigidbody2D>();
                if (rb == null) errors.Add($"{name}: falta Rigidbody2D");
                else if (rb.bodyType == RigidbodyType2D.Kinematic && !rb.useFullKinematicContacts)
                    errors.Add($"{name}: Use Full Kinematic Contacts desactivado");
            }

            // Sprites importados (Fase 5): arrays del Meteor y nave del jugador
            var meteorAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/Meteor.prefab");
            if (meteorAsset != null)
            {
                var mSo = new SerializedObject(meteorAsset.GetComponent<Meteor>());
                foreach (var f in new[] { "bigRedSprites", "bigGreySprites", "smallSprites" })
                {
                    var p = mSo.FindProperty(f);
                    if (p == null || p.arraySize == 0 ||
                        p.GetArrayElementAtIndex(0).objectReferenceValue == null)
                        errors.Add($"Meteor.{f} sin sprites (¿falta ejecutar la Fase 5?)");
                }
            }
            if (player != null)
            {
                var pSprite = player.GetComponent<SpriteRenderer>().sprite;
                if (pSprite == null || pSprite.name != "nave")
                    errors.Add("El jugador no usa el sprite nave.png (¿falta la Fase 5?)");
            }

            // Iconos de power-ups (Fase 6)
            var puAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/PowerUp.prefab");
            if (puAsset != null)
            {
                var puSo = new SerializedObject(puAsset.GetComponent<PowerUpItem>());
                var arrP = puSo.FindProperty("typeSprites");
                if (arrP == null || arrP.arraySize != 5 ||
                    arrP.GetArrayElementAtIndex(0).objectReferenceValue == null)
                    errors.Add("PowerUpItem.typeSprites sin los 5 iconos (¿falta la Fase 6?)");
            }

            return errors;
        }
    }

    private static void CheckRef(Object target, string field,
        System.Collections.Generic.List<string> errors)
    {
        var prop = new SerializedObject(target).FindProperty(field);
        if (prop == null || prop.objectReferenceValue == null)
            errors.Add($"{target.GetType().Name}.{field} sin asignar");
    }

    // ------------------------- Infraestructura -------------------------

    private static void EnsureTags(params string[] tags)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tagsProp = tagManager.FindProperty("tags");

        foreach (var tag in tags)
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) { exists = true; break; }
            if (!exists)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            }
        }
        tagManager.ApplyModifiedProperties();
    }

    private static void EnsureFolders()
    {
        foreach (var dir in new[] { SpritesDir, PrefabsDir, ScenesDir })
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", dir.Substring("Assets/".Length));
    }

    // --------------------- Generación de sprites -----------------------
    // Dibuja cada figura en una textura de 256px con supermuestreo 2x2
    // (antialiasing) y la importa como sprite de 1 unidad de mundo.

    private static Sprite MakeSprite(string name, System.Func<float, float, bool> inside)
    {
        const int SIZE = 256;
        var tex = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
        var pixels = new Color32[SIZE * SIZE];

        for (int y = 0; y < SIZE; y++)
        for (int x = 0; x < SIZE; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < 2; sy++)
            for (int sx = 0; sx < 2; sx++)
            {
                float u = (x + 0.25f + sx * 0.5f) / SIZE - 0.5f;
                float v = (y + 0.25f + sy * 0.5f) / SIZE - 0.5f;
                if (inside(u, v)) hits++;
            }
            pixels[y * SIZE + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 4));
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        string path = $"{SpritesDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = SIZE;   // 256px = 1 unidad de mundo
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static float Cross(float ax, float ay, float bx, float by) => ax * by - ay * bx;

    private static bool PointInTri(float px, float py, float ax, float ay,
                                   float bx, float by, float cx, float cy)
    {
        float d1 = Cross(bx - ax, by - ay, px - ax, py - ay);
        float d2 = Cross(cx - bx, cy - by, px - bx, py - by);
        float d3 = Cross(ax - cx, ay - cy, px - cx, py - cy);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static bool InRegularPolygon(float u, float v, int sides, float radius)
    {
        float dist = Mathf.Sqrt(u * u + v * v);
        if (dist < 1e-6f) return true;
        float segment = 2f * Mathf.PI / sides;
        float local = Mathf.Repeat(Mathf.Atan2(v, u), segment) - segment / 2f;
        float maxDist = radius * Mathf.Cos(segment / 2f) / Mathf.Cos(local);
        return dist <= maxDist;
    }

    private static bool InCapsule(float u, float v)
    {
        const float halfW = 0.2f, straight = 0.28f;
        if (Mathf.Abs(u) <= halfW && Mathf.Abs(v) <= straight) return true;
        float dy = Mathf.Abs(v) - straight;
        return dy >= 0 && u * u + dy * dy <= halfW * halfW;
    }

    // --------------------------- Prefabs -------------------------------

    private static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{PrefabsDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    private static Rigidbody2D AddKinematicBody(GameObject go)
    {
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        // Sin esto, dos cuerpos Kinematic NO generan eventos de trigger
        // entre sí (bala vs meteorito). Es el detalle de físicas 2D más
        // fácil de olvidar en el montaje manual.
        rb.useFullKinematicContacts = true;
        return rb;
    }

    private static GameObject MakeBulletPrefab(Sprite capsule)
    {
        var go = new GameObject("Bullet") { tag = "Bullet" };
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = capsule;
        sr.color = ColBullet;
        go.transform.localScale = new Vector3(0.25f, 0.35f, 1f);

        var col = go.AddComponent<CapsuleCollider2D>();
        col.isTrigger = true;
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(0.4f, 0.96f);   // ajustado al dibujo de la cápsula

        AddKinematicBody(go);
        go.AddComponent<Bullet>();
        return SavePrefab(go, "Bullet");
    }

    private static GameObject MakeMeteorPrefab(Sprite hexagon)
    {
        var go = new GameObject("Meteor") { tag = "Meteor" };
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = hexagon;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.4f;   // ~0.8x del visual: hitbox "justa" como el prototipo

        AddKinematicBody(go);
        go.AddComponent<Meteor>();
        return SavePrefab(go, "Meteor");
    }

    private static GameObject MakeItemPrefab(Sprite diamond)
    {
        var go = new GameObject("Item") { tag = "Item" };
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = diamond;
        sr.color = ColItem;
        go.transform.localScale = new Vector3(0.45f, 0.45f, 1f);

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        AddKinematicBody(go);
        go.AddComponent<CollectibleItem>();
        return SavePrefab(go, "Item");
    }

    private static GameObject MakeStarPrefab(Sprite circle)
    {
        var go = new GameObject("Star");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circle;
        sr.color = new Color(1f, 1f, 1f, 0.35f);
        sr.sortingOrder = -10;   // siempre detrás de todo
        return SavePrefab(go, "Star");
    }

    // ---------------------------- Escena -------------------------------

    private static void BuildScene(GameConfig config, GameObject bulletPrefab,
        GameObject meteorPrefab, GameObject itemPrefab, GameObject starPrefab,
        Sprite triangle)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- Cámara ---
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7.4f;           // mundo 420x740 del prototipo / 50
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ColBg;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.AddComponent<AudioListener>();

        // --- GameManager ---
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        SetField(gm, "config", config);

        // --- Pools (a nivel raíz: los objetos reciclados NO deben ser
        //     hijos de nada que se mueva, como el Player) ---
        var bulletPool = MakePool("BulletPool", bulletPrefab, 16);
        var meteorPool = MakePool("MeteorPool", meteorPrefab, 24);
        var itemPool   = MakePool("ItemPool", itemPrefab, 8);

        // --- Spawner ---
        var spawnerGo = new GameObject("Spawner");
        var spawner = spawnerGo.AddComponent<Spawner>();
        SetField(spawner, "meteorPool", meteorPool);
        SetField(spawner, "itemPool", itemPool);

        // --- Player ---
        var playerGo = new GameObject("Player") { tag = "Player" };
        playerGo.transform.position = new Vector3(0f, -6.0f, 0f);
        playerGo.transform.localScale = new Vector3(0.6f, 0.7f, 1f);
        var playerSr = playerGo.AddComponent<SpriteRenderer>();
        playerSr.sprite = triangle;
        playerSr.color = ColShip;

        var playerCol = playerGo.AddComponent<CircleCollider2D>();
        playerCol.isTrigger = true;
        playerCol.radius = 0.35f;
        playerCol.offset = new Vector2(0f, -0.05f);

        var playerRb = playerGo.AddComponent<Rigidbody2D>();
        playerRb.bodyType = RigidbodyType2D.Dynamic;   // Dynamic garantiza triggers
        playerRb.gravityScale = 0f;
        playerRb.freezeRotation = true;

        playerGo.AddComponent<PlayerController>();

        var muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(playerGo.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        var shooter = playerGo.AddComponent<Shooter>();
        SetField(shooter, "bulletPool", bulletPool);
        SetField(shooter, "muzzle", muzzle.transform);

        // --- Fondo de estrellas ---
        var starfieldGo = new GameObject("Starfield");
        var starfield = starfieldGo.AddComponent<Starfield>();
        SetField(starfield, "starPrefab", starPrefab);

        // --- UI ---
        BuildUI();

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static SimplePool MakePool(string name, GameObject prefab, int capacity)
    {
        var go = new GameObject(name);
        var pool = go.AddComponent<SimplePool>();
        SetField(pool, "prefab", prefab);
        var so = new SerializedObject(pool);
        so.FindProperty("initialCapacity").intValue = capacity;
        so.ApplyModifiedPropertiesWithoutUndo();
        return pool;
    }

    private static void BuildUI()
    {
        // Canvas escalado a referencia 1080x1920 (móvil vertical)
        var canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        var hud = canvasGo.AddComponent<HUDController>();

        // Marcador (solo visible jugando)
        var scoreText = MakeText(canvasGo.transform, "ScoreText", "0", 100, ColText, true,
            new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(600f, 130f));

        // ---- Pantalla de inicio ----
        var startPanel = MakePanel(canvasGo.transform, "StartPanel", null);
        MakeText(startPanel.transform, "TitleText", "ASTRO DASH", 110, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(1000f, 160f));
        MakeText(startPanel.transform, "HintText",
            "Arrastra para moverte · Disparo automático\nEsquiva las rocas y atrapa las estrellas",
            40, ColTextDim, false,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(1000f, 140f));
        var startBest = MakeText(startPanel.transform, "BestText", "", 44, ColItem, false,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(600f, 80f));
        MakeText(startPanel.transform, "PlayText", "TOCA PARA JUGAR", 56, ColShip, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -640f), new Vector2(800f, 100f));

        // ---- Pantalla de game over (con velo oscuro) ----
        var gameOverPanel = MakePanel(canvasGo.transform, "GameOverPanel",
            new Color(7f / 255f, 11f / 255f, 20f / 255f, 0.75f));
        MakeText(gameOverPanel.transform, "TitleText", "FIN DEL VIAJE", 90, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(1000f, 140f));
        var finalScore = MakeText(gameOverPanel.transform, "FinalScoreText", "0", 150, ColShip, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(800f, 200f));
        var overBest = MakeText(gameOverPanel.transform, "BestText", "", 44, ColItem, false,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(600f, 80f));
        MakeText(gameOverPanel.transform, "RetryText", "TOCA PARA REINTENTAR", 52, ColText, true,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -640f), new Vector2(900f, 100f));

        gameOverPanel.SetActive(false);

        // Cablear el HUD
        SetField(hud, "scoreText", scoreText);
        SetField(hud, "startPanel", startPanel);
        SetField(hud, "startBestText", startBest);
        SetField(hud, "gameOverPanel", gameOverPanel);
        SetField(hud, "finalScoreText", finalScore);
        SetField(hud, "gameOverBestText", overBest);

        // EventSystem (por si luego se añaden botones)
        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();
    }

    private static GameObject MakePanel(Transform parent, string name, Color? overlay)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;      // ocupa toda la pantalla
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        if (overlay.HasValue)
        {
            var img = go.AddComponent<Image>();   // velo semitransparente
            img.color = overlay.Value;
        }
        return go;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text,
        float size, Color color, bool bold, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    // Asigna un campo [SerializeField] privado a través del sistema de
    // serialización del editor (igual que arrastrar en el Inspector)
    private static void SetField(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
            throw new System.Exception($"Campo '{fieldName}' no encontrado en {target.GetType().Name}");
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
