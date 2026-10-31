Shader "Custom/ParkingAura"
{
    Properties
    {
        [HDR] _BaseColor("Aura Color", Color) = (1.0, 0.85, 0.1, 1.0)
        _Intensity("Intensity", Range(0.1, 10.0)) = 2.2
        _FadePower("Vertical Fade Power", Range(0.5, 6.0)) = 1.6
        _GroundGlowBoost("Ground Glow Boost", Range(0.0, 6.0)) = 2.5
        _WaveSpeed("Wave Speed", Range(0.0, 10.0)) = 3.0
        _WaveFreq("Wave Frequency", Range(1.0, 30.0)) = 12.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 positionWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Intensity;
                float _FadePower;
                float _GroundGlowBoost;
                float _WaveSpeed;
                float _WaveFreq;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInputs.positionCS;
                output.positionWS = vertexInputs.positionWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float y = saturate(input.uv.y);
                float x = saturate(input.uv.x);

                // Gradiente vertical: máximo en el suelo (y=0), disolviéndose suavemente hacia arriba (y=1)
                float verticalFade = pow(saturate(1.0 - y), _FadePower);

                // Línea láser brillante concentrada exactamente en el contacto con el asfalto
                float groundLine = exp(-y * 22.0) * _GroundGlowBoost;

                // Ondas de energía ascendentes continuas (aura holográfica)
                float wave = 0.82 + 0.18 * sin(y * _WaveFreq - _Time.y * _WaveSpeed);

                // Sutiles líneas de escaneo vertical holográfico
                float scan = 0.90 + 0.10 * sin(x * 50.0);

                // Suavizado en las esquinas para conectar limpiamente paredes perpendiculares
                float sideFade = smoothstep(0.0, 0.05, x) * smoothstep(1.0, 0.95, x);

                float totalFactor = (verticalFade + groundLine) * wave * scan * sideFade;
                half3 finalRgb = _BaseColor.rgb * (_Intensity * totalFactor);

                return half4(finalRgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

