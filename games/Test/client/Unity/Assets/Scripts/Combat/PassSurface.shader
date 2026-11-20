Shader "Test/PassSurface"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Surface", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = .25
        _Glow ("Emission", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex;
        fixed4 _Color;
        half _Metallic;
        half _Smoothness;
        fixed4 _Glow;
        struct Input { float2 uv_MainTex; };
        void surf(Input input, inout SurfaceOutputStandard surface)
        {
            surface.Albedo = tex2D(_MainTex, input.uv_MainTex).rgb * _Color.rgb;
            surface.Metallic = _Metallic;
            surface.Smoothness = _Smoothness;
            surface.Emission = _Glow.rgb;
            surface.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
