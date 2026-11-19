Shader "Test/Particles/Soft Additive"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        ColorMask RGB
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "UnityCG.cginc"

            struct ParticleVertex
            {
                float4 position : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vertex(ParticleVertex input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 center = 2.0 * input.uv - 1.0;
                float distanceSquared = dot(center, center);
                float glow = 1.0 - smoothstep(0.0, 1.0, distanceSquared);
                float core = 1.0 - smoothstep(0.0, 0.16, distanceSquared);
                return half4((input.color.rgb * glow * glow + core * 0.33) * input.color.a, 0.0);
            }
            ENDCG
        }
    }
}
