using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainScript : MonoBehaviour
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
    MaterialPropertyBlock propsCylinder;
    MaterialPropertyBlock propsCubes;
    MaterialPropertyBlock propsParticles;

    public ComputeShader compute;
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
    ComputeBuffer particlePosLocalBuffer; // (x,y,z,bodyId)
    Vector4[] particlePosLocalArray;
    ComputeBuffer particlePosRelativeBuffer; 
    ComputeBuffer particlePosWorldBuffer; 
    ComputeBuffer particleVelBuffer; 
    ComputeBuffer particleForceBuffer; 
    ComputeBuffer particleTorqueBuffer; 
    ComputeBuffer colorBuffer;
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

    public float stiffness = 1700;
    public float damping = 6;
    public float friction = 2;
    public float drag = .3f;
    public float sphereRadius;
    public Vector3 spherePosition = new Vector3();

    int maxParticles;
    int maxBodies;
    float time = 0;
    float fixedTime = 0;

    public Vector3 gravity = new Vector3(0, -2, 0);
    Vector3 maxVelocity = new Vector3(100000, 100000, 100000);
    public int maxSubSteps = 1;
    float accumulator = 0;
    int [] _tempInt = { 0, 0 }; // used to avoid GC memory allocation

    void Start()
    {
        propsCylinder = new MaterialPropertyBlock();
        propsCubes = new MaterialPropertyBlock();
        propsParticles = new MaterialPropertyBlock();

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
        interactionSphereMeshInstance.transform.localScale = new Vector3(sphereRadius, sphereRadius, sphereRadius);
        //interactionSphereMeshInstance.transform.localScale = new Vector3(sphereRadius*0.5f,sphereRadius*0.5f,sphereRadius*0.5f);
        interactionSphereMeshInstance.SetActive(true);

        _drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer.SetData(new uint[5] { BodyMesh1.GetIndexCount(0), (uint)InstanceCount / 2, 0, 0, 0 });

        _drawArgsBuffer2 = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer2.SetData(new uint[5] { BodyMesh2.GetIndexCount(0), (uint)InstanceCount / 2, 0, 0, (uint)(InstanceCount / 2) });

        _drawArgsBuffer3 = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _drawArgsBuffer3.SetData(new uint[5] { particleMesh.GetIndexCount(0), (uint)maxParticles, 0, 0, 0 });

        propsCylinder.SetVector("radius", new Vector3(2 * radius, 4 * radius, 2 * radius));
        propsCylinder.SetBuffer("_TransformBuffer", bodyPosBuffer);
        propsCylinder.SetBuffer("_QuatBuffer", bodyQuatBuffer);
        propsCylinder.SetBuffer("Color", colorBuffer);

        propsCubes.SetVector("radius", new Vector3(4 * radius, 2 * radius, 4 * radius));
        //propsCubes.SetVector("radius", new Vector3(2*radius, 4*radius,2*radius));
        propsCubes.SetBuffer("_TransformBuffer", bodyPosBuffer);
        propsCubes.SetBuffer("_QuatBuffer", bodyQuatBuffer);
        propsCubes.SetBuffer("Color", colorBuffer);

        propsParticles.SetVector("radius", new Vector3(2 * radius, 2 * radius, 2 * radius));
        propsParticles.SetBuffer("_TransformBuffer", particlePosWorldBuffer);
        propsParticles.SetBuffer("_QuatBuffer", bodyQuatBuffer);
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
        compute.SetInts("Iteration", new int[] { numParticles, numParticles });
        compute.SetFloats("cellSize", new float[] { radius * 2, radius * 2, radius * 2 });
        compute.SetFloats("gridPos", new float[] { gridPosition.x, gridPosition.y, gridPosition.z });
        compute.SetFloats("gridResolution", new float[] { gridResolution.x, gridResolution.y, gridResolution.z });
        compute.SetFloats("boxSize", new float[] { boxSize.x, boxSize.y, boxSize.z });
        compute.SetFloat("friction", friction);
        compute.SetFloat("drag", drag);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("damping", damping);
        compute.SetFloat("radius", radius);

        compute.SetFloat("deltaTime", fixedTimeStep);
        compute.SetFloat("time", time);

        compute.SetFloats("interactionSpherePos", new float[] { spherePosition.x, spherePosition.y, spherePosition.z });
        compute.SetFloat("interactionSphereRadius", sphereRadius);

        compute.SetFloats("gravity", new float[] { gravity.x, gravity.y, gravity.z });
        compute.SetFloats("maxVelocity", new float[] { maxVelocity.x, maxVelocity.y, maxVelocity.z });

        updatePhysics();

        if (!renderParticles)
        {
            Graphics.DrawMeshInstancedIndirect(
                BodyMesh1, 0, bodyMaterial, DrawMeshBounds,
                _drawArgsBuffer, 0, propsCylinder
            );
            Graphics.DrawMeshInstancedIndirect(
                BodyMesh2, 0, bodyMaterial, DrawMeshBounds,
                _drawArgsBuffer2, 0, propsCubes
            );
        }
        else
        {
            Graphics.DrawMeshInstancedIndirect(
            particleMesh, 0, particleMaterial, DrawMeshBounds,
            _drawArgsBuffer3, 0, propsParticles
            );
        }
    }

    void updatePhysics()
    {

        float deltaTime = Time.deltaTime;
        float introSweepPos = Mathf.Max(0, 1 - fixedTime);
        float x = 0.22f * Mathf.Sin(1 * 1.9f * fixedTime);
        float y = 0.01f + (0.0f * (Mathf.Cos(1 * 2 * fixedTime) + 0.5f) + introSweepPos);
        //float y = 0.2f*(Mathf.Cos(1 * 2 * fixedTime)+0.5f) + introSweepPos;
        float z = 0.22f * Mathf.Cos(1 * 2.1f * fixedTime) + introSweepPos;
        interactionSphereMeshInstance.transform.position = new Vector3(x, y, z);
        spherePosition.Set(x, y, z);
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
        var kernel = compute.FindKernel("UpdateParticles");
        _tempInt[0] = numParticles; _tempInt[1] = numParticles;
        compute.SetInts("Iteration", _tempInt);
        compute.SetBuffer(kernel, "particlePosLocalBuffer", particlePosLocalBuffer);
        compute.SetBuffer(kernel, "bodyPosBuffer", bodyPosBuffer);
        compute.SetBuffer(kernel, "bodyQuatBuffer", bodyQuatBuffer);
        compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        compute.SetBuffer(kernel, "bodyVelBuffer", bodyVelBuffer);
        compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);
        compute.SetBuffer(kernel, "particleVelocityBuffer", particleVelBuffer);
        compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);
    }

    void clearGrid()
    {
        var kernel = compute.FindKernel("ClearGrid");
        _tempInt[0] = numParticles; _tempInt[1] = numParticles * numParticles;
        compute.SetInts("Iteration", _tempInt);
        compute.SetBuffer(kernel, "gridBuffer", gridBuffer);
        compute.Dispatch(kernel, ThreadGroupCount3, 1, 1);

    }

    void updateGrid()
    {
        var kernel = compute.FindKernel("UpdateGrid");
         _tempInt[0] = numParticles; _tempInt[1] = numParticles;
        compute.SetInts("Iteration", _tempInt);
        compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        compute.SetBuffer(kernel, "gridBuffer", gridBuffer);

        compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);

    }

    void updateParticlesForces()
    {
        var kernel = compute.FindKernel("UpdateParticlesForces");
         _tempInt[0] = numParticles; _tempInt[1] = numParticles;
        compute.SetInts("Iteration", _tempInt);
        compute.SetBuffer(kernel, "particlePosWorldBuffer", particlePosWorldBuffer);
        compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        compute.SetBuffer(kernel, "particleVelocityBuffer", particleVelBuffer);
        compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);
        compute.SetBuffer(kernel, "gridBuffer", gridBuffer);
        compute.SetBuffer(kernel, "particleForceBuffer", particleForceBuffer);

        compute.SetBuffer(kernel, "particleTorqueBuffer", particleTorqueBuffer);

        compute.Dispatch(kernel, ThreadGroupCount2, 1, 1);
    }

    void addParticlesForcesToBody()
    {
        var kernel = compute.FindKernel("AddParticlesForcesToBody");
        _tempInt[0] = numBodies; _tempInt[1] = numBodies;
        compute.SetInts("Iteration", _tempInt);
        compute.SetBuffer(kernel, "particleForceBuffer", particleForceBuffer);
        compute.SetBuffer(kernel, "bodyForceBuffer", bodyForceBuffer);

        compute.SetBuffer(kernel, "particleTorqueBuffer", particleTorqueBuffer);
        compute.SetBuffer(kernel, "particlePosRelativeBuffer", particlePosRelativeBuffer);
        compute.SetBuffer(kernel, "bodyTorqueBuffer", bodyTorqueBuffer);

        compute.Dispatch(kernel, ThreadGroupCount, 1, 1);
    }

    void updateBody()
    {
        var kernel = compute.FindKernel("UpdateBody");
        _tempInt[0] = numBodies; _tempInt[1] = numBodies;
        compute.SetInts("Iteration", _tempInt);
        compute.SetFloat("linearAngular", 0.0f);

        compute.SetBuffer(kernel, "bodyQuatBuffer", bodyQuatBuffer);
        compute.SetBuffer(kernel, "bodyForceBuffer", bodyForceBuffer);
        compute.SetBuffer(kernel, "bodyVelBuffer", bodyVelBuffer);
        compute.SetBuffer(kernel, "bodyMassBuffer", bodyMassBuffer);

        compute.SetBuffer(kernel, "bodyTorqueBuffer", bodyTorqueBuffer);
        compute.SetBuffer(kernel, "bodyAngularVelBuffer", bodyAngularVelBuffer);

        compute.SetBuffer(kernel, "bodyPosBuffer", bodyPosBuffer);

        compute.Dispatch(kernel, ThreadGroupCount, 1, 1);
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