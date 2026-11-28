Shader "BBads/UI/Card Progress Fill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Fill Color", Color) = (1,1,1,1)
        _InteriorStart ("Interior Start", Range(0, 1)) = 0.45
        _InteriorEnd ("Interior End", Range(0, 1)) = 0.70
        _InsetPixels ("Interior Inset (Pixels)", Range(0, 6)) = 3
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _InteriorStart;
            float _InteriorEnd;
            float _InsetPixels;
            float4 _MainTex_TexelSize;

            half InteriorAt(float2 uv)
            {
                fixed4 sampleColor = tex2D(_MainTex, uv) + _TextureSampleAdd;
                half luminance = dot(sampleColor.rgb, half3(0.299h, 0.587h, 0.114h));
                return smoothstep(_InteriorStart, _InteriorEnd, luminance) * step(0.001h, sampleColor.a);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 source = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                float2 inset = _MainTex_TexelSize.xy * _InsetPixels;
                half interior = InteriorAt(input.texcoord);
                interior = min(interior, InteriorAt(input.texcoord + float2(inset.x, 0)));
                interior = min(interior, InteriorAt(input.texcoord - float2(inset.x, 0)));
                interior = min(interior, InteriorAt(input.texcoord + float2(0, inset.y)));
                interior = min(interior, InteriorAt(input.texcoord - float2(0, inset.y)));
                interior = min(interior, InteriorAt(input.texcoord + inset));
                interior = min(interior, InteriorAt(input.texcoord - inset));
                interior = min(interior, InteriorAt(input.texcoord + float2(inset.x, -inset.y)));
                interior = min(interior, InteriorAt(input.texcoord + float2(-inset.x, inset.y)));
                fixed4 color;
                color.rgb = lerp(source.rgb, input.color.rgb, interior);
                color.a = source.a * input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
