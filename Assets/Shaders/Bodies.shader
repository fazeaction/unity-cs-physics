Shader "Custom/Cube" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf Standard vertex:vert addshadow nolightmap
        #pragma instancing_options procedural:setup
        #pragma target 4.5

		sampler2D _MainTex;

		struct Input {
			float2 uv_MainTex;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED

        StructuredBuffer<float4> _TransformBuffer;
        StructuredBuffer<float4> _QuatBuffer;
        StructuredBuffer<float3> Color;
        half3 radius;

        #endif

        
        float3 applyQuat(float3 v, float4 q){
            float ix =  q.w * v.x + q.y * v.z - q.z * v.y;
            float iy =  q.w * v.y + q.z * v.x - q.x * v.z;
            float iz =  q.w * v.z + q.x * v.y - q.y * v.x;
            float iw = -q.x * v.x - q.y * v.y - q.z * v.z;
            return float3(
                ix * q.w + iw * -q.x + iy * -q.z - iz * -q.y,
                iy * q.w + iw * -q.y + iz * -q.x - ix * -q.z,
                iz * q.w + iw * -q.z + ix * -q.y - iy * -q.x
            );
        }   

		void setup()
        {
            #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED

            uint id = unity_InstanceID;

            // Retrieve a transformation from TransformBuffer.
            float4 ps = _TransformBuffer[id];
            
            // Object to world matrix.
            float3 v1 = applyQuat(float3(radius.x,0.0,0.0), _QuatBuffer[id]);
            float3 v2 = applyQuat(float3(0.0,radius.y,0.0), _QuatBuffer[id]);
            float3 v3 = applyQuat(float3(0.0,0.0,radius.z), _QuatBuffer[id]);

            float4x4 o2w = float4x4(
                v1.x, v2.x, v3.x, ps.x,
                v1.y, v2.y, v3.y, ps.y,
                v1.z, v2.z, v3.z, ps.z,
                0, 0, 0, 1
            );

            float4x4 w2o = float4x4(
                v1.x, v1.y, v1.z, -ps.x,
                v2.x, v2.y, v2.z, -ps.x,
                v3.x, v3.y, v3.z, -ps.x,
                0, 0, 0, 1
            );

            unity_ObjectToWorld = o2w;
            unity_WorldToObject = w2o;
            _Color = float4(Color[id],1);

            #endif
        }


		// Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
		// See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
		// #pragma instancing_options assumeuniformscaling
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)

		void vert(inout appdata_full v, out Input data)
        {
            UNITY_INITIALIZE_OUTPUT(Input, data);
        }

		void surf (Input IN, inout SurfaceOutputStandard o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			// Metallic and smoothness come from slider variables
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			o.Alpha = c.a;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
