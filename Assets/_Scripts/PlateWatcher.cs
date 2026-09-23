using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Surveille une assiette : si elle tombe trop loin en dessous de sa
/// position de départ (donc qu'elle est tombée du plateau), déclenche
/// l'événement onDropped. À poser sur le GameObject "Plate".
/// </summary>
public class PlateWatcher : MonoBehaviour
{
    [Tooltip("Distance verticale (en dessous du point de départ) à partir de laquelle l'assiette est considérée comme tombée.")]
    public float dropDistance = 3f;

    public UnityEvent onDropped;

    private Vector3 startPos;
    private bool dropped;

    private void Start()
    {
        startPos = transform.position;
    }

    private void Update()
    {
        if (!dropped && transform.position.y < startPos.y - dropDistance)
        {
            dropped = true;
            onDropped.Invoke();
        }
    }

    /// <summary>Remet l'assiette à son point de départ (à appeler après une chute, par ex. depuis onDropped).</summary>
    public void ResetPlate()
    {
        transform.position = startPos;
        transform.rotation = Quaternion.identity;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        dropped = false;
    }
}
