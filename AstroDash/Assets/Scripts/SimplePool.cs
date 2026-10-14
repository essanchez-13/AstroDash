using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// OBJECT POOLING — la optimización más importante al pasar a móvil.
///
/// En el prototipo HTML creábamos y destruíamos objetos JS sin costo real.
/// En Unity, Instantiate/Destroy cada frame genera basura de memoria y el
/// recolector (GC) provoca micro-tirones visibles en móvil. Un pool crea
/// los objetos UNA vez y los recicla: en lugar de destruir, se desactivan
/// y vuelven a la reserva para el siguiente uso.
///
/// Uso: añade este componente a un GameObject vacío, asigna el prefab,
/// y llama a Get(posicion) para "spawnear". El objeto se devuelve solo
/// mediante PooledObject.Release().
/// </summary>
public class SimplePool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField] private int initialCapacity = 16;

    private ObjectPool<GameObject> pool;

    private void Awake()
    {
        pool = new ObjectPool<GameObject>(
            createFunc: CreateInstance,
            actionOnGet: null,              // la activación la hace Get() TRAS posicionar
            actionOnRelease: go => go.SetActive(false),
            actionOnDestroy: go => { if (go != null) Destroy(go); },
            collectionCheck: true,          // avisa si algo se libera dos veces
            defaultCapacity: initialCapacity);
    }

    // Sin esto el pool quedaba vivo hasta que el recolector de basura lo
    // finalizaba DESPUÉS de salir del modo Play: su Clear() llamaba a
    // Destroy ya en modo edición → "Destroy may not be called from edit
    // mode" en la consola por cada objeto reciclado.
    private void OnDestroy() => pool?.Dispose();

    private GameObject CreateInstance()
    {
        // Los instanciamos como hijos de este objeto para tener la
        // jerarquía ordenada durante el desarrollo
        var go = Instantiate(prefab, transform);

        // PooledObject guarda la referencia a su pool de origen para
        // poder devolverse solo, sin que nadie tenga que buscarlo
        var pooled = go.GetComponent<PooledObject>();
        if (pooled == null) pooled = go.AddComponent<PooledObject>();
        pooled.Init(this);
        return go;
    }

    public GameObject Get(Vector3 position)
    {
        var go = pool.Get();
        // POSICIONAR ANTES DE ACTIVAR: si se activara aún en su posición
        // vieja (donde "murió" la vez anterior), los OnEnable leerían
        // coordenadas rancias — se veían fantasmas de un frame: ítems
        // anclados a la X antigua, partículas "de la nada", etc.
        go.transform.position = position;
        go.SetActive(true);
        return go;
    }

    public void Release(GameObject go) => pool.Release(go);
}
