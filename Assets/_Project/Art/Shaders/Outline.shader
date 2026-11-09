// 외곽선용 셰이더. 메시를 오브젝트 중심에서 바깥쪽으로 _Width(m)만큼 부풀리고 뒷면만 그린다.
// 렌더러의 머티리얼 목록 끝에 추가하면 원래 모습 바깥에 테두리가 생긴다.
// 상자·원기둥처럼 중심이 가운데 있는 볼록한 모양에 맞춰져 있다.
Shader "BombDefuse/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.06, 0.06, 1)
        _Width ("Width (m)", Float) = 0.008
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // 월드 공간에서 부풀려서, 오브젝트 Scale이 달라도 테두리 두께가 같게 한다
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                positionWS += SafeNormalize(positionWS - centerWS) * _Width;

                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
