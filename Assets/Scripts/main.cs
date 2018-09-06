using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{

    float ySpread = 0.1f;
    Vector3 gridPosition = new Vector3();
    Vector3 gridResolution = new Vector3();
    public GameObject boundsSimulationMesh;
    int numBodies;
    Vector3 boxSize;

    public int numParticles = 64;
    public bool renderParticles = true;
    [SerializeField] Mesh BodyMesh1;
    [SerializeField] Mesh BodyMesh2;
    [SerializeField] Mesh particleMesh;
    [SerializeField] GameObject interactionSphereMesh;
    GameObject interactionSphereMeshInstance;

    public Mesh template
    {
        get { return BodyMesh1; }
        set { BodyMesh1 = value; ReallocateBuffer(); }
    }
    public Mesh template2
    {
        get { return BodyMesh2; }
        set { BodyMesh2 = value; ReallocateBuffer(); }
    }
    public Mesh template3
    {
        get { return particleMesh; }
        set { particleMesh = value; ReallocateBuffer(); }
    }
    public Material bodyMaterial;
    public Material particleMaterial;
    MaterialPropertyBlock _props;
    MaterialPropertyBlock _props2;
    MaterialPropertyBlock _props3;

    public ComputeShader _compute;
    ComputeBuffer _drawArgsBuffer;
    ComputeBuffer _drawArgsBuffer2;
    ComputeBuffer _drawArgsBuffer3;

    //body buffers
    ComputeBuffer bodyPosBuffer;
    Vector4[] bodyPosArray;
    ComputeBuffer bodyQuatBuffer;
    Vector4[] bodyQuatArray;
    ComputeBuffer bodyVelBuffer;
    ComputeBuffer bodyAngularVelBuffer;
    ComputeBuffer bodyForceBuffer;
    ComputeBuffer bodyTorqueBuffer;
    ComputeBuffer bodyMassBuffer;

    Vector4[] bodyMassArray;


    //particles buffers
    ComputeBuffer particlePosLocalBuffer; // (x,y,z,bodyId)
    Vector4[] particlePosLocalArray;
    ComputeBuffer particlePosRelativeBuffer; // (x,y,z,bodyId)

    ComputeBuffer particlePosWorldBuffer; // (x,y,z,bodyId)
    ComputeBuffer particleVelBuffer; // (x,y,z,1)
    ComputeBuffer particleForceBuffer; // (x,y,z,1)
    ComputeBuffer particleTorqueBuffer; // (x,y,z,1)

    ComputeBuffer colorBuffer; // (x,y,z,bodyId)
    Vector3[] colorArray;

    // Broadphase
    ComputeBuffer gridBuffer;

    const int kThreadCount = 128;
    int InstanceCount { get { return numBodies * numBodies; } }
    int ThreadGroupCount
    {
        get { return (InstanceCount + kThreadCount - 1) / kThreadCount; }
    }
    int ThreadGroupCount2
    {
        get { return ((numParticles * numParticles) + kThreadCount - 1) / kThreadCount; }
    }
    int ThreadGroupCount3
    {
        get { return ((numParticles * numParticles * numParticles) + kThreadCount - 1) / kThreadCount; }
    }
    int TotalThreadCount
    {
        get { return ThreadGroupCount * kThreadCount; }
    }
    int TotalThreadCount2
    {
        get { return ThreadGroupCount2 * kThreadCount; }
    }
    int TotalThreadCount3
    {
        get { return ThreadGroupCount3 * kThreadCount; }
    }

    private Bounds DrawMeshBounds;

    int bodyCount = 0;
    int particleCount = 0;
    float _radius = 0.5f;
    public float radius
    {
        get { return _radius; }
        set
        {
            _radius = value;
            updateMaxVelocity();
        }
    }

    [SerializeField] float _fixedTimeStep = 1.0f / 60.0f;
    public float fixedTimeStep
    {
        get { return _fixedTimeStep; }
        set
        {
            _fixedTimeStep = value;
            updateMaxVelocity();
        }
    }

    [SerializeField] float _stiffness = 1700;
    public float stiffness
    {
        get { return _stiffness; }
        set { _stiffness = value; }
    }

    [SerializeField] float _damping = 6;
    public float damping
    {
        get { return _damping; }
        set { _damping = value; }
    }

    [SerializeField] float _friction = 2;
    public float friction
    {
        get { return _friction; }
        set { _friction = value; }
    }

    [SerializeField] float _drag = .3f;
    public float drag
    {
        get { return _drag; }
        set { _drag = value; }
    }

    [SerializeField] float _sphereRadius = .2f;
    public float sphereRadius
    {
        get { return _sphereRadius; }
        set { _sphereRadius = value; }
    }

    [SerializeField] Vector3 _spherePosition = new Vector3();
    public Vector3 spherePosition
    {
        get { return _spherePosition; }
        set { _spherePosition = value; }
    }

    int maxParticles;
    int maxBodies;

    float time = 0;
    float fixedTime = 0;
    private Vector3 broadphasePosition = new Vector3(0, 0, 0);
    private Vector3 broadphaseResolution = new Vector3(64, 64, 64);
    Vector2 broadphaseGridZTiling = new Vector2(0.0f, 0.0f);


    public Vector3 gravity = new Vector3(0, -2, 0);
    Vector3 maxVelocity = new Vector3(100000, 100000, 100000);
    public int maxSubSteps = 1;
    float accumulator = 0;
    float interpolationValue = 0;
    float gridBufferW;
    float gridBufferH;

    void Start()
    {
        _props = new MaterialPropertyBlock();
        _props2 = new MaterialPropertyBlock();
        _props3 = new MaterialPropertyBlock();

        radius = 1.0f / numParticles * 0.5f;
        boxSize = new Vector3(0.25f * 0.9f, 7, 0.25f * 0.9f);
        gridResolution.Set(numParticles, numParticles, numParticles);
        gridPosition.Set(-boxSize.x, 0, -boxSize.z);

        boundsSimulationMesh.transform.localScale = new Vector3(boxSize.x * 2.0f, boxSize.y * 0.5f, boxSize.z * 2.0f);
        boundsSimulationMesh.transform.position = new Vector3(0, 0, 0);
        numBodies = numParticles / 2;
        maxParticles = numParticles * numParticles;
        maxBodies = numBodies * numBodies;

        bodyPosBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyQuatBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyVelBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyForceBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyTorqueBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyAngularVelBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        bodyMassBuffer = new ComputeBuffer(maxBodies, 4 * sizeof(float));
        colorBuffer = new ComputeBuffer(maxBodies, 3 * sizeof(float));
        bodyPosArray = new Vector4[maxBodies];
        bodyQuatArray = new Vector4[maxBodies];
        bodyMassArray = new Vector4[maxBodies];
        colorArray = new Vector3[maxBodies];
        for (int bodyId = 0; bodyId < maxBodies; bodyId++)
        {
            float x = -boxSize.x + 2.0f * boxSize.x * Random.Range(0.0f, 1.0f);
            float y = ySpread * Random.Range(0.0f, 1.0f);
            float z = -boxSize.z + 2.0f * boxSize.z * Random.Range(0.0f, 1.0f);

            var q = new Quaternion();
            var axis = new Vector3(
                Random.Range(0.0f, 1.0f) - 0.5f,
                Random.Range(0.0f, 1.0f) - 0.5f,
                Random.Range(0.0f, 1.0f) - 0.5f
            );
            axis.Normalize();
            q.SetAxisAngle(axis, Random.Range(0.0f, 1.0f) * Mathf.PI * 2.0f);

            Vector3 inertia = new Vector3(0.0f, 0.0f, 0.0f);
            float mass = 1.0f;
            if (bodyId < maxBodies / 2)
            {
                calculateBoxInertia(ref inertia, mass, new Vector3(radius * 4.0f, radius * 4.0f, radius * 2.0f));
            }
            else
            {
                calculateBoxInertia(ref inertia, mass, new Vector3(radius * 2.0f * 4.0f, radius * 2, radius * 2.0f));
            }

            addBody(x, y, z, q.x, q.y, q.z, q.w, mass, inertia.x, inertia.y, inertia.z);
        }

        bodyPosBuffer.SetData(bodyPosArray);
        bodyQuatBuffer.SetData(bodyQuatArray);
        bodyMassBuffer.SetData(bodyMassArray);
        colorBuffer.SetData(colorArray);

        particlePosWorldBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particlePosRelativeBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particlePosLocalBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particleVelBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particleForceBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particleTorqueBuffer = new ComputeBuffer(maxParticles, 4 * sizeof(float));
        particlePosLocalArray = new Vector4[maxParticles];

        gridBufferW = (2 * broadphaseResolution.x * broadphaseGridZTiling.x);
        gridBufferH = (2 * broadphaseResolution.z * broadphaseGridZTiling.y);

        gridBuffer = new ComputeBuffer((int)(numParticles * numParticles * numParticles), 4 * sizeof(float));

        // Add particles to bodies
        for (int particleId = 0; particleId < maxParticles; ++particleId)
        {
            int bodyId = Mathf.FloorToInt(particleId / 4);
            //int bodyId = particleId;//Mathf.FloorToInt(particleId / 4);

            float x = 0.0f, y = 0.0f, z = 0.0f;

            if (bodyId < maxBodies / 2)
            {
                y = (particleId % 4 - 1.5f) * radius * 2.01f;
            }
            else
            {

                var i = particleId - bodyId * 4;
                x = ((i % 2) - 0.5f) * radius * 2.01f;
                z = (Mathf.Floor(i / 2) - 0.5f) * radius * 2.01f;
            }
            addParticle(bodyId, x, y, z);
        }

        particlePosLocalBuffer.SetData(particlePosLocalArray);

        interactionSphereMeshInstance = Instantiate(interactionSphereMesh);
        interactionSphereMeshInstance.transform.localScale = new Vector3(_sphereRadius * 2 * 0.5f, _sphereRadius * 2 * 0.5f, _sphereRadius * 2 * 0.5f);
        //interactionSphereMeshInstance.transform.localScale = new Vector3(_sphereRadius*2.0f,_sphereRadius*2.0f,_sphereRadius*2.0f);
        interactionSphereMeshInstance.SetActive(true);

        _drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer.SetData(new uint[5] { BodyMesh1.GetIndexCount(0), (uint)InstanceCount / 2, 0, 0, 0 });

        _drawArgsBuffer2 = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer2.SetData(new uint[5] { BodyMesh2.GetIndexCount(0), (uint)InstanceCount / 2, 0, 0, (uint)(InstanceCount / 2) });

        _drawArgsBuffer3 = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer3.SetData(new uint[5] { particleMesh.GetIndexCount(0), (uint)maxParticles, 0, 0, 0 });

        _props.SetVector("radius", new Vector3(2 * radius, 4 * radius, 2 * radius));
        _props.SetBuffer("_TransformBuffer", bodyPosBuffer);
        _props.SetBuffer("_QuatBuffer", bodyQuatBuffer);
        _props.SetBuffer("Color", colorBuffer);

        _props2.SetVector("radius", new Vector3(4 * radius, 2 * radius, 4 * radius));
        //_props2.SetVector("radius", new Vector3(2*radius, 4*radius,2*radius));
        _props2.SetBuffer("_TransformBuffer", bodyPosBuffer);
        _props2.SetBuffer("_QuatBuffer", bodyQuatBuffer);
        _props2.SetBuffer("Color", colorBuffer);

        _props3.SetVector("radius", new Vector3(2 * radius, 2 * radius, 2 * radius));
        _props3.SetBuffer("_TransformBuffer", particlePosWorldBuffer);
        _props3.SetBuffer("_QuatBuffer", bodyQuatBuffer);
    }

    void calculateBoxInertia(ref Vector3 inertia, float mass, Vector3 extents)
    {
        float c = 1.0f / 12.0f * mass;
        inertia.Set(
            c * (2 * extents.y * 2 * extents.y + 2 * extents.z * 2 * extents.z),
            c * (2 * extents.x * 2 * extents.x + 2 * extents.z * 2 * extents.z),
            c * (2 * extents.y * 2 * extents.y + 2 * extents.x * 2 * extents.x)
        );


    }

    void ReallocateBuffer()
    {
        if (_drawArgsBuffer != null)
        {
            _drawArgsBuffer.Release();
            _drawArgsBuffer = null;
        }

        if (_drawArgsBuffer2 != null)
        {
            _drawArgsBuffer2.Release();
            _drawArgsBuffer2 = null;
        }

        if (_drawArgsBuffer3 != null)
        {
            _drawArgsBuffer3.Release();
            _drawArgsBuffer3 = null;
        }

    }

    // Update is called once per frame
    void Update()
    {
        _compute.SetInts("Iteration", new int[] { numParticles, numParticles });
        _compute.SetFloats("cellSize", new float[] { radius * 2, radius * 2, radius * 2 });
        _compute.SetFloats("gridPos", new float[] { gridPosition.x, gridPosition.y, gridPosition.z });
        _compute.SetFloats("gridResolution", new float[] { gridResolution.x, gridResolution.y, gridResolution.z });
        _compute.SetFloats("boxSize", new float[] { boxSize.x, boxSize.y, boxSize.z });
        _compute.SetFloat("friction", friction);
        _compute.SetFloat("drag", drag);
        _compute.SetFloat("stiffness", stiffness);
        _compute.SetFloat("damping", damping);
        _compute.SetFloat("radius", radius);

        _compute.SetFloat("deltaTime", fixedTimeStep);
        _compute.SetFloat("time", time);

        _compute.SetFloats("interactionSpherePos", new float[] { spherePosition.x, spherePosition.y, spherePosition.z });
        _compute.SetFloat("interactionSphereRadius", _sphereRadius);

        _compute.SetFloats("gravity", new float[] { gravity.x, gravity.y, gravity.z });
        _compute.SetFloats("maxVelocity", new float[] { maxVelocity.x, maxVelocity.y, maxVelocity.z });

        updatePhysics();

        if (!renderParticles)
        {
            Graphics.DrawMeshInstancedIndirect(
                BodyMesh1, 0, bodyMaterial, DrawMeshBounds,
                _drawArgsBuffer, 0, _props
            );
            Graphics.DrawMeshInstancedIndirect(
                BodyMesh2, 0, bodyMaterial, DrawMeshBounds,
                _drawArgsBuffer2, 0, _props2
            );
        }
        else
        {
            Graphics.DrawMeshInstancedIndirect(
            particleMesh, 0, particleMaterial, DrawMeshBounds,
            _drawArgsBuffer3, 0, _props3
            );
        }
    }

    float prevTime, prevSpawnedBody = 0;
    void updatePhysics()
    {

        float deltaTime = Time.deltaTime;
        float introSweepPos = Mathf.Max(0, 1 - fixedTime);
        float x = 0.22f * Mathf.Sin(1 * 1.9f * fixedTime);
        float y = 0.01f + (0.0f * (Mathf.Cos(1 * 2 * fixedTime) + 0.5f) + introSweepPos);
        //float y = 0.2f*(Mathf.Cos(1 * 2 * fixedTime)+0.5f) + introSweepPos;
        float z = 0.22f * Mathf.Cos(1 * 2.1f * fixedTime) + introSweepPos;
        interactionSphereMeshInstance.transform.position = new Vector3(x, y, z);
        _spherePosition.Set(x, y, z);
        fixedTime += fixedTimeStep;
        step(deltaTime);
    }

    void updateMaxVelocity()
    {
        // Set max velocity so that we don't get too much overlap between 2 particles in one time step

        var v = 2 * radius / fixedTimeStep;
        maxVelocity.Set(v, v, v);
    }

    void step(float deltaTime)
    {
        var tempAccumulator = accumulator;
        //var fixedTimeStep = this.fixedTimeStep;

        tempAccumulator += deltaTime;
        var substeps = 0;
        while (tempAccumulator >= fixedTimeStep)
        {
            // Do fixed steps to catch up
            if (substeps < maxSubSteps)
            {
                singleStep();
            }
            tempAccumulator -= fixedTimeStep;
            substeps++;
        }

        interpolationValue = tempAccumulator / fixedTimeStep;
        time += deltaTime;
        accumulator = tempAccumulator;
    }
    void singleStep()
    {
        updateParticles();
        clearGrid();
        updateGrid();
        updateParticlesForces();
        addParticlesForcesToBody();
        updateBody();
        fixedTime += fixedTimeStep;
    }

    void updateParticles()
    {
        var kernel = _compute.FindKernel("UpdateParticles");
        _compute.SetInts("Iteration", new int[] { numParticles, numParticles });
        _compute.SetBuffer(kernel, "particlePosLocalBuffer", particlePosLocalBuffer);
        _compute.SetBuffer(kernel, "bodyPosBuffer", bodyPosBuffer);
        _compute.SetBuffer(kernel, "bodyQuatBuffer", bodyQuatBuffer);
        _compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        _compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        _compute.SetBuffer(kernel, "bodyVelBuffer", bodyVelBuffer);
        _compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);
        _compute.SetBuffer(kernel, "particleVelocityBuffer", particleVelBuffer);
        _compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);
    }

    void clearGrid()
    {
        var kernel = _compute.FindKernel("ClearGrid");
        _compute.SetInts("Iteration", new int[] { numParticles, numParticles * numParticles });
        _compute.SetBuffer(kernel, "gridBuffer", gridBuffer);
        _compute.Dispatch(kernel, ThreadGroupCount3, 1, 1);

    }

    void updateGrid()
    {
        var kernel = _compute.FindKernel("UpdateGrid");
        _compute.SetInts("Iteration", new int[] { numParticles, numParticles * numParticles });
        _compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        _compute.SetBuffer(kernel, "gridBuffer", gridBuffer);

        _compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);

    }

    void updateParticlesForces()
    {
        var kernel = _compute.FindKernel("UpdateParticlesForces");
        _compute.SetInts("Iteration", new int[] { numParticles, numParticles });
        _compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        _compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        _compute.SetBuffer(kernel, "particleVelocityBuffer", particleVelBuffer);
        _compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);
        _compute.SetBuffer(kernel, "gridBuffer", gridBuffer);
        _compute.SetBuffer(kernel, "particleForceBuffer", particleForceBuffer);

        _compute.SetBuffer(kernel, "particleTorqueBuffer", particleTorqueBuffer);

        _compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);
    }

    void addParticlesForcesToBody()
    {
        var kernel = _compute.FindKernel("AddParticlesForcesToBody");
        _compute.SetInts("Iteration", new int[] { numBodies, numBodies });
        _compute.SetBuffer(kernel, "particleForceBuffer", particleForceBuffer);
        _compute.SetBuffer(kernel, "bodyForceBuffer", bodyForceBuffer);

        _compute.SetBuffer(kernel, "particleTorqueBuffer", particleTorqueBuffer);
        _compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        _compute.SetBuffer(kernel, "bodyTorqueBuffer", bodyTorqueBuffer);

        _compute.Dispatch(kernel, ThreadGroupCount, 1, 1);
    }

    void updateBody()
    {
        var kernel = _compute.FindKernel("UpdateBody");
        _compute.SetInts("Iteration", new int[] { numBodies, numBodies });
        _compute.SetFloat("linearAngular", 0.0f);

        _compute.SetBuffer(kernel, "bodyQuatBuffer", bodyQuatBuffer);
        _compute.SetBuffer(kernel, "bodyForceBuffer", bodyForceBuffer);
        _compute.SetBuffer(kernel, "bodyVelBuffer", bodyVelBuffer);
        _compute.SetBuffer(kernel, "bodyMassBuffer", bodyMassBuffer);

        _compute.SetBuffer(kernel, "bodyTorqueBuffer", bodyTorqueBuffer);
        _compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);

        _compute.SetBuffer(kernel, "bodyPosBuffer", bodyPosBuffer);

        _compute.Dispatch(kernel, ThreadGroupCount, 1, 1);
    }

    int addBody(float x, float y, float z, float qx, float qy, float qz, float qw, float mass, float inertiaX, float inertiaY, float inertiaZ)
    {
        // Position
        bodyPosArray[bodyCount] = new Vector4(x, y, z, 1);

        // Quaternion
        bodyQuatArray[bodyCount] = new Vector4(qx, qy, qz, qw);

        // Mass
        bodyMassArray[bodyCount] = new Vector4(1 / inertiaX, 1 / inertiaY, 1 / inertiaZ, 1 / mass);

        // Mass
        float r = Random.Range(0.0f, 1.0f);
        float g = Random.Range(0.0f, 1.0f);
        float b = Random.Range(0.0f, 1.0f);
        colorArray[bodyCount] = new Vector3(r, g, b);

        return bodyCount++;
    }

    int addParticle(int bodyId, float x, float y, float z)
    {
        // Position
        particlePosLocalArray[particleCount] = new Vector4(x, y, z, bodyId);

        return particleCount++;
    }

    void OnDestroy()
    {
        bodyPosBuffer.Release();
        bodyQuatBuffer.Release();
        bodyVelBuffer.Release();
        bodyForceBuffer.Release();
        bodyTorqueBuffer.Release();
        bodyAngularVelBuffer.Release();
        bodyMassBuffer.Release();

        particlePosWorldBuffer.Release();
        particlePosLocalBuffer.Release();
        particlePosRelativeBuffer.Release();
        particleVelBuffer.Release();
        particleForceBuffer.Release();
        particleTorqueBuffer.Release();

        gridBuffer.Release();

        _drawArgsBuffer.Release();
        _drawArgsBuffer2.Release();
        _drawArgsBuffer3.Release();
    }
}