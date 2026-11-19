Shader "Test/Particles/Soft Additive"
{
    Properties
    {
        _Sparkle ("Star Shape", Range(0, 1)) = 0
    }

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

            half _Sparkle;

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
                float2 axes = abs(center);
                float rays = (1.0 - smoothstep(0.0, 0.22, min(axes.x, axes.y))) * glow;
                float shape = lerp(glow * glow, max(rays, 0.65 * core), _Sparkle);
                return half4((input.color.rgb * shape + core * 0.33) * input.color.a, 0.0);
            }
            ENDCG
        }
    }
}
