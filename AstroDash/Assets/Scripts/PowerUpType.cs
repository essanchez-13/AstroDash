/// <summary>
/// Los cuatro power-ups del juego. El orden importa: se usa como índice
/// en los temporizadores de PlayerPowerUps y en los colores/letras de
/// PowerUpItem, igual que las claves del objeto `power` del prototipo.
/// </summary>
public enum PowerUpType
{
    Shield = 0,      // Escudo: turbo hacia adelante + destruye meteoritos al embestir
    Magnet = 1,      // Imán: atrae los ítems cercanos
    DoubleShot = 2,  // Disparo doble: dos proyectiles por ráfaga
    RapidFire = 3,   // Disparo rápido: el doble de cadencia
    BigShot = 4,     // Potencia: bala grande con doble daño
}
