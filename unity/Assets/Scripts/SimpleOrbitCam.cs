using UnityEngine;

// Drop this on the Main Camera so you can look around in Play mode.
// Left-mouse drag = orbit, scroll = zoom. (In the Editor you can also just use
// the Scene view to fly around without this.)
public class SimpleOrbitCam : MonoBehaviour
{
    public Transform target;
    public float distance = 9f, yaw = 35f, pitch = 30f, speed = 130f;

    void Start()
    {
        if (target == null)
        {
            var rb = FindObjectOfType<RoomBuilder>();
            if (rb != null) target = rb.transform;
        }
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            yaw   += Input.GetAxis("Mouse X") * speed * Time.deltaTime;
            pitch -= Input.GetAxis("Mouse Y") * speed * Time.deltaTime;
            pitch  = Mathf.Clamp(pitch, 5f, 85f);
        }
        distance = Mathf.Clamp(distance - Input.mouseScrollDelta.y * 1.5f, 3f, 25f);

        Vector3 t = (target ? target.position : Vector3.zero) + Vector3.up * 1.2f;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        transform.position = t + rot * new Vector3(0, 0, -distance);
        transform.LookAt(t);
    }
}
