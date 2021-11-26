using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RayTracingMain : MonoBehaviour {
    public ComputeShader RayTracingShader;
    public RenderTexture renderTexture;
    public Light DirectionalLight;

    public Camera mainCamera;
    public Texture texture;
    public Vector3 curseurPosition;
    public float speed;

    private uint _currentSample = 0;
    private Material _addMaterial;
    public Shader shader;
    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    public uint SpheresMax = 100;
    public float SpherePlacementRadius = 100.0f;

    private ComputeBuffer sphereBuffer;

    private Sphere selectedSphere;



    struct Sphere {
        public Vector3 center;
        public float radius;
        public Vector4 albedo;
        public Vector3 specular;
    };

    private void Awake() {
        mainCamera = GetComponent<Camera>();
    }

    private void SetShaderParameters() {
        RayTracingShader.SetMatrix("_CameraToWorld", mainCamera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", mainCamera.projectionMatrix.inverse);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));

        Vector3 l = DirectionalLight.transform.forward;
        RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));
        RayTracingShader.SetBuffer(0, "_Spheres", sphereBuffer);
    }

    // Start is called before the first frame update
    private void OnRenderImage(RenderTexture src, RenderTexture dst) {
        SetShaderParameters();

        // Releases the current target if it exists, but does not have the size of the screen
        if (renderTexture != null) {
            renderTexture.Release();
        }

        // Get a render target for Ray Tracing
        renderTexture = new RenderTexture(Screen.width, Screen.height, 0);
        renderTexture.enableRandomWrite = true;
        renderTexture.Create();


        // Set the texture for the shader and dispatch the computer shader
        RayTracingShader.SetTexture(0, "Result", renderTexture);
        RayTracingShader.SetTexture(0, "SkyboxTexture", texture);
        RayTracingShader.SetFloat("Resolution", renderTexture.width);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        RayTracingShader.SetVector("LightPosition", DirectionalLight.transform.position);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);


        Graphics.Blit(renderTexture, dst);

        // Blit the result texture to the screen
        //if (_addMaterial == null)
        //{
        //    _addMaterial = new Material(shader);
        //}
        //_addMaterial.SetFloat("_Sample", _currentSample);
        //Graphics.Blit(renderTexture, dst, _addMaterial);
        //_currentSample++;
    }
    private void Update() {
        if (transform.hasChanged) {
            _currentSample = 0;
            transform.hasChanged = false;
        }

        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.D)) {
            speed = Time.time;
        }

        if (Input.GetKey(KeyCode.Q)) {
            mainCamera.transform.position -= Vector3.right * (Time.time - speed) * (Time.time - speed);
        }
        if (Input.GetKey(KeyCode.D)) {
            mainCamera.transform.position += Vector3.right * (Time.time - speed) * (Time.time - speed);
        }
        if (Input.GetKey(KeyCode.Z)) {
            mainCamera.transform.position += Vector3.forward * (Time.time - speed) * (Time.time - speed);
        }
        if (Input.GetKey(KeyCode.S)) {
            mainCamera.transform.position -= Vector3.forward * (Time.time - speed) * (Time.time - speed);
        }
        if (Input.GetKey(KeyCode.A)) {
            mainCamera.transform.position += Vector3.up;
        }
        if (Input.GetKey(KeyCode.E) && mainCamera.transform.position.y > 0) {
            mainCamera.transform.position -= Vector3.up;
        }

        if (Input.GetMouseButtonDown(0)) {
            curseurPosition = Input.mousePosition;
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        }
        if (Input.GetMouseButton(0)) {
            mainCamera.transform.Rotate(new Vector3(Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0));
        }

    }
    private void OnEnable() {
        _currentSample = 0;
        SetUpScene();
    }
    private void OnDisable() {
        if (sphereBuffer != null) {
            sphereBuffer.Release();
        }
    }
    private void SetUpScene() {
        List<Sphere> spheres = GenerateRandomSpheres();

        // Assign to compute buffer
        int stride = sizeof(float) * 3 * 2 + sizeof(float) + sizeof(float) * 4;
        sphereBuffer = new ComputeBuffer(spheres.Count, stride); // stride = [sizeof(float) * 3] * 3 + sizeof(float) = size of all variabels in a sphere
        sphereBuffer.SetData(spheres);
    }

    List<Sphere> GenerateRandomSpheres() {
        List<Sphere> spheres = new List<Sphere>();
        // Add a number of random spheres
        for (int i = 0; i < SpheresMax; i++) {
            Sphere sphere = new Sphere();

            // Radius and center
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            sphere.center = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            // Reject spheres that are intersecting others
            foreach (Sphere other in spheres) {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.center - other.center) < minDist * minDist) {
                    goto SkipSphere;
                }
            }

            // Albedo and specular color
            Color color = Random.ColorHSV();
            bool metal = Random.value < 0.5f;
            sphere.albedo = metal ? Vector4.zero : new Vector4(color.r, color.g, color.b, 1.0f);
            sphere.specular = metal ? new Vector3(color.r, color.g, color.b) : Vector3.one * 0.04f;

            // Add the sphere to the list
            spheres.Add(sphere);

        SkipSphere:
            continue;
        }

        return spheres;
    }



    void IntersectSphere(Ray ray, Sphere sphere) {
        // Intersection between a line and a segment follow a quadratic equation
        // The unknown parameter is the distance of the ray to the origin
        Vector3 distSphereToOrigin = ray.origin - sphere.center;

        // Calculation of the discriminant, check Wikipedia
        // dot = scalar product
        // a = 1
        float b = Vector3.Dot(ray.direction, distSphereToOrigin); // Value of Wikipedia /2
        float c = Vector3.Dot(distSphereToOrigin, distSphereToOrigin) - sphere.radius * sphere.radius;
        float d = b * b - c; // discriminant = (2b)* (2b) - 4ac, so everything is divided by 4

        if (d < 0) {
            return;
        }
        
        else {
            sphere.albedo -= new Vector4(0, 0, 0, (Mathf.Cos(Time.time) + 1) / 4 );

        }
    }
}