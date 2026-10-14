using UnityEngine;

/// <summary>
/// Etiqueta que llevan todos los objetos reciclables (balas, meteoritos,
/// ítems). Recuerda a qué pool pertenece para poder devolverse con una
/// sola llamada: GetComponent&lt;PooledObject&gt;().Release().
/// SimplePool lo añade automáticamente al crear cada instancia.
/// </summary>
public class PooledObject : MonoBehaviour
{
    private SimplePool owner;

    public void Init(SimplePool pool) => owner = pool;

    public void Release()
    {
        // Guarda: si ya está desactivado, ya fue devuelto — evitar doble release
        if (!gameObject.activeSelf) return;
        owner.Release(gameObject);
    }
}
