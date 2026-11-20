Shader "Test/PassEffects"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Vertex { float4 position : POSITION; fixed4 color : COLOR; };
            struct Raster { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Raster vert(Vertex vertex)
            {
                Raster output;
                output.position = UnityObjectToClipPos(vertex.position);
                output.color = vertex.color;
                return output;
            }
            fixed4 frag(Raster input) : SV_Target { return input.color; }
            ENDCG
        }
    }
}
