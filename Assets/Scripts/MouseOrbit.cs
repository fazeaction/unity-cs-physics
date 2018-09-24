using UnityEngine;
using System.Collections;

[AddComponentMenu("Camera-Control/Mouse Orbit with zoom")]
public class MouseOrbit : MonoBehaviour {

    public Transform target;
    public float distance = 5.0f;
    public float easing = 0.2f;
    public float sensitivity = 1.0f;
    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;

    public float distanceMin = .5f;
    public float distanceMax = 15f;

    float x = 0.0f;
    float y = 0.0f;

    Quaternion rotation;
    
    Vector3 position;

    private Vector3 currentRotation;
    private Vector3 targetRotation;
    private float targetDistance;
    private Vector3 negDistance;
    private Vector3 velocity = Vector3.zero;


    // Use this for initialization
    void Start ()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;

        targetRotation.Set(y, x, 0);
        currentRotation.Set(y, x, 0);
        targetDistance = distance;
    }


    void LateUpdate ()
    {

        if (target && (Input.GetButton("Fire1") || Input.mouseScrollDelta.y != 0))
        {
            x += Input.GetAxis("Mouse X") * sensitivity;
            y -= Input.GetAxis("Mouse Y") * sensitivity;

            targetDistance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel"), distanceMin, distanceMax);
        }

        
    }

    void Update() {
            y = ClampAngle(y, yMinLimit, yMaxLimit);
            targetRotation.Set(y, x, 0);
            currentRotation = Vector3.SmoothDamp(currentRotation,targetRotation,ref velocity, easing);
            rotation = Quaternion.Euler(currentRotation);

            distance+=(targetDistance-distance)*easing;
            negDistance.Set(0.0f, 0.0f, -distance);
            position = rotation * negDistance + target.position;

            transform.rotation = rotation;
            transform.position =  position;
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360F)
            angle += 360F;
        if (angle > 360F)
            angle -= 360F;
        return Mathf.Clamp(angle, min, max);
    }
}