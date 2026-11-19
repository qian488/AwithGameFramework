Shader "Test/Particles/Bolt"
{
    Properties
    {
        _Intensity ("Flash Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One One
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half _Intensity;

            struct BoltVertex
            {
                float4 position : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct BoltFragment
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            BoltFragment vert(BoltVertex input)
            {
                BoltFragment output;
                output.position = UnityObjectToClipPos(input.position);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            fixed4 frag(BoltFragment input) : SV_Target
            {
                float edge = abs(2.0 * input.uv.y - 1.0);
                float glow = 1.0 - smoothstep(0.0, 1.0, edge);
                float core = 1.0 - smoothstep(0.15, 0.4, edge);
                return fixed4((input.color.rgb * glow + 0.7 * core) * input.color.a * _Intensity, 0.0);
            }
            ENDCG
        }
    }
}
