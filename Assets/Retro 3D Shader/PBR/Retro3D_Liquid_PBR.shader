// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Shader created with Shader Forge v1.40 
// Shader Forge (c) Freya Holmer - http://www.acegikmo.com/shaderforge/
// Note: Manually altering this data may prevent you from opening it in Shader Forge
/*SF_DATA;ver:1.40;sub:START;pass:START;ps:flbk:,iptp:0,cusa:False,bamd:0,cgin:,cpap:True,lico:1,lgpr:1,limd:3,spmd:0,trmd:0,grmd:0,uamb:True,mssp:True,bkdf:True,hqlp:True,rprd:True,enco:False,rmgx:True,imps:True,rpth:1,vtps:0,hqsc:True,nrmq:1,nrsp:0,vomd:0,spxs:False,tesm:0,olmd:1,culm:2,bsrc:0,bdst:1,dpts:2,wrdp:True,dith:1,atcv:False,rfrpo:True,rfrpn:Refraction,coma:15,ufog:True,aust:True,igpj:False,qofs:0,qpre:2,rntp:3,fgom:False,fgoc:False,fgod:False,fgor:False,fgmd:0,fgcr:0.5,fgcg:0.5,fgcb:0.5,fgca:1,fgde:0.01,fgrn:0,fgrf:300,stcl:False,atwp:False,stva:128,stmr:255,stmw:255,stcp:6,stps:0,stfa:0,stfz:0,ofsf:0,ofsu:0,f2p0:False,fnsp:False,fnfb:False,fsmp:False;n:type:ShaderForge.SFN_Final,id:4013,x:32869,y:32712,varname:node_4013,prsc:2|diff-6219-OUT,spec-2207-OUT,gloss-1014-OUT,normal-5247-OUT,emission-6219-OUT,clip-300-OUT,voffset-9581-XYZ;n:type:ShaderForge.SFN_Color,id:1304,x:32174,y:32775,ptovrint:False,ptlb:Color,ptin:_Color,varname:node_1304,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,c1:1,c2:1,c3:1,c4:1;n:type:ShaderForge.SFN_FragmentPosition,id:5440,x:31426,y:32983,cmnt:Vertex snapping - Screen Space,varname:node_5440,prsc:2;n:type:ShaderForge.SFN_Transform,id:3472,x:31635,y:32983,varname:node_3472,prsc:2,tffrom:0,tfto:3|IN-5440-XYZ;n:type:ShaderForge.SFN_Multiply,id:1173,x:31816,y:33072,varname:node_1173,prsc:2|A-3472-XYZ,B-5489-OUT;n:type:ShaderForge.SFN_Round,id:5805,x:31981,y:33072,varname:node_5805,prsc:2|IN-1173-OUT;n:type:ShaderForge.SFN_Subtract,id:6209,x:32165,y:33072,varname:node_6209,prsc:2|A-5805-OUT,B-1173-OUT;n:type:ShaderForge.SFN_Divide,id:414,x:32335,y:33072,varname:node_414,prsc:2|A-6209-OUT,B-5489-OUT;n:type:ShaderForge.SFN_Transform,id:9581,x:32496,y:33072,varname:node_9581,prsc:2,tffrom:3,tfto:0|IN-414-OUT;n:type:ShaderForge.SFN_Tex2d,id:109,x:32174,y:32583,ptovrint:False,ptlb:Diffuse,ptin:_Diffuse,varname:node_109,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:0,isnm:False|UVIN-8950-OUT;n:type:ShaderForge.SFN_Multiply,id:1980,x:32383,y:32700,varname:node_1980,prsc:2|A-109-RGB,B-1304-RGB,C-6289-RGB;n:type:ShaderForge.SFN_Rotator,id:6623,x:33066,y:32259,varname:node_6623,prsc:2|UVIN-8455-UVOUT,ANG-4959-OUT;n:type:ShaderForge.SFN_Slider,id:7293,x:31992,y:32334,ptovrint:False,ptlb:Swirl Amount,ptin:_SwirlAmount,varname:node_7293,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-4,cur:-2,max:4;n:type:ShaderForge.SFN_TexCoord,id:8455,x:31544,y:32586,varname:node_8455,prsc:2,uv:0,uaff:False;n:type:ShaderForge.SFN_Subtract,id:4229,x:31793,y:32435,varname:node_4229,prsc:2|A-8455-UVOUT,B-1271-OUT;n:type:ShaderForge.SFN_Vector1,id:1271,x:31793,y:32368,varname:node_1271,prsc:2,v1:0.5;n:type:ShaderForge.SFN_Divide,id:2166,x:31987,y:32435,varname:node_2166,prsc:2|A-4229-OUT,B-1271-OUT;n:type:ShaderForge.SFN_Abs,id:5350,x:32174,y:32435,varname:node_5350,prsc:2|IN-2166-OUT;n:type:ShaderForge.SFN_Length,id:4400,x:32353,y:32435,varname:node_4400,prsc:2|IN-5350-OUT;n:type:ShaderForge.SFN_OneMinus,id:5186,x:32520,y:32435,varname:node_5186,prsc:2|IN-4400-OUT;n:type:ShaderForge.SFN_Clamp01,id:866,x:32691,y:32435,varname:node_866,prsc:2|IN-5186-OUT;n:type:ShaderForge.SFN_Multiply,id:2111,x:32691,y:32294,varname:node_2111,prsc:2|A-866-OUT,B-7293-OUT,C-1239-OUT;n:type:ShaderForge.SFN_Time,id:5790,x:31800,y:32045,varname:node_5790,prsc:2;n:type:ShaderForge.SFN_Multiply,id:2289,x:32546,y:32094,varname:node_2289,prsc:2|A-5790-T,B-9996-OUT;n:type:ShaderForge.SFN_Slider,id:3074,x:31992,y:32233,ptovrint:False,ptlb:Outer Swirl Speed,ptin:_OuterSwirlSpeed,varname:_Swirl_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-1,cur:0,max:1;n:type:ShaderForge.SFN_Frac,id:6678,x:32714,y:32094,varname:node_6678,prsc:2|IN-2289-OUT;n:type:ShaderForge.SFN_Multiply,id:1955,x:32875,y:32094,varname:node_1955,prsc:2|A-6678-OUT,B-8910-OUT;n:type:ShaderForge.SFN_Tau,id:8910,x:32747,y:31983,varname:node_8910,prsc:2;n:type:ShaderForge.SFN_Add,id:4959,x:32875,y:32294,varname:node_4959,prsc:2|A-2111-OUT,B-1955-OUT;n:type:ShaderForge.SFN_Multiply,id:8963,x:31992,y:32045,varname:node_8963,prsc:2|A-6964-OUT,B-5790-T;n:type:ShaderForge.SFN_Sin,id:4218,x:32164,y:32045,varname:node_4218,prsc:2|IN-8963-OUT;n:type:ShaderForge.SFN_RemapRange,id:1239,x:32341,y:32045,varname:node_1239,prsc:2,frmn:-1,frmx:1,tomn:0,tomx:1|IN-4218-OUT;n:type:ShaderForge.SFN_Slider,id:6964,x:31643,y:31961,ptovrint:False,ptlb:Inner Swirl Speed,ptin:_InnerSwirlSpeed,cmnt:Inner Swirl Sine Wave,varname:_OuterSwirlSpeed_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:-1,cur:0,max:1;n:type:ShaderForge.SFN_RemapRange,id:9996,x:32341,y:32202,varname:node_9996,prsc:2,frmn:-0.1,frmx:0.1,tomn:-0.01,tomx:0.01|IN-3074-OUT;n:type:ShaderForge.SFN_Slider,id:2207,x:31773,y:32716,ptovrint:False,ptlb:Specular,ptin:_Specular,varname:node_2207,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0,max:1;n:type:ShaderForge.SFN_Slider,id:4927,x:31773,y:32824,ptovrint:False,ptlb:Gloss,ptin:_Gloss,varname:node_4927,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0,max:1;n:type:ShaderForge.SFN_Multiply,id:1014,x:32383,y:32570,varname:node_1014,prsc:2|A-4927-OUT,B-109-A,C-6289-A;n:type:ShaderForge.SFN_Slider,id:5427,x:31017,y:32799,ptovrint:False,ptlb:Alpha Clip,ptin:_AlphaClip,varname:_AlphaClip_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0.25,max:1;n:type:ShaderForge.SFN_RemapRange,id:8561,x:31356,y:32773,varname:node_8561,prsc:2,frmn:0,frmx:1,tomn:0,tomx:4|IN-5427-OUT;n:type:ShaderForge.SFN_Power,id:300,x:31547,y:32773,varname:node_300,prsc:2|VAL-109-A,EXP-8561-OUT;n:type:ShaderForge.SFN_ValueProperty,id:7612,x:31984,y:33557,ptovrint:False,ptlb:Normal Intensity,ptin:_NormalIntensity,varname:node_7251,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:1;n:type:ShaderForge.SFN_Vector3,id:543,x:32165,y:33463,varname:node_543,prsc:2,v1:0,v2:0,v3:1;n:type:ShaderForge.SFN_OneMinus,id:6573,x:32165,y:33557,varname:node_6573,prsc:2|IN-7612-OUT;n:type:ShaderForge.SFN_Lerp,id:5247,x:32391,y:33289,varname:node_5247,prsc:2|A-750-RGB,B-543-OUT,T-6573-OUT;n:type:ShaderForge.SFN_Tex2d,id:750,x:32165,y:33291,ptovrint:True,ptlb:Normal,ptin:_BumpMap,varname:_BumpMap,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:3,isnm:True;n:type:ShaderForge.SFN_ValueProperty,id:5489,x:31635,y:33196,ptovrint:False,ptlb:Vertex Detail,ptin:_VertexDetail,varname:node_5489,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:80;n:type:ShaderForge.SFN_ViewVector,id:6661,x:31190,y:33298,varname:node_6661,prsc:2;n:type:ShaderForge.SFN_Negate,id:9944,x:31379,y:33298,varname:node_9944,prsc:2|IN-6661-OUT;n:type:ShaderForge.SFN_Reflect,id:4352,x:31559,y:33381,varname:node_4352,prsc:2|A-9944-OUT,B-6905-OUT;n:type:ShaderForge.SFN_NormalVector,id:6905,x:31379,y:33459,prsc:2,pt:False;n:type:ShaderForge.SFN_Cubemap,id:6289,x:31762,y:33381,ptovrint:False,ptlb:Cubemap,ptin:_Cubemap,varname:node_6289,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,pvfc:0|DIR-4352-OUT,MIP-2537-OUT;n:type:ShaderForge.SFN_Slider,id:2537,x:31222,y:33649,ptovrint:False,ptlb:Falloff,ptin:_Falloff,varname:node_2537,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0,max:10;n:type:ShaderForge.SFN_Posterize,id:6219,x:32574,y:32700,varname:node_6219,prsc:2|IN-1980-OUT,STPS-5922-OUT;n:type:ShaderForge.SFN_ValueProperty,id:5922,x:32574,y:32850,ptovrint:False,ptlb:Colour Depth,ptin:_ColourDepth,varname:node_5922,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:32;n:type:ShaderForge.SFN_ValueProperty,id:9663,x:31124,y:32498,ptovrint:False,ptlb:Y Scroll,ptin:_YScroll,varname:_XScroll_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_ValueProperty,id:4896,x:31124,y:32398,ptovrint:False,ptlb:X Scroll,ptin:_XScroll,varname:node_344,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_Append,id:190,x:31333,y:32426,varname:node_190,prsc:2|A-4896-OUT,B-9663-OUT;n:type:ShaderForge.SFN_Multiply,id:4989,x:31322,y:32188,varname:node_4989,prsc:2|A-190-OUT,B-5790-T;n:type:ShaderForge.SFN_Add,id:5831,x:31322,y:31981,varname:node_5831,prsc:2|A-8455-UVOUT,B-4989-OUT;n:type:ShaderForge.SFN_Add,id:8950,x:31889,y:32563,varname:node_8950,prsc:2|A-6623-UVOUT,B-5831-OUT;proporder:109-1304-7293-6964-3074-2207-4927-5489-5427-750-7612-6289-2537-5922-9663-4896;pass:END;sub:END;*/

Shader "Retro3D/PBR/Retro3D_Liquid_PBR" {
    Properties {
        _Diffuse ("Diffuse", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SwirlAmount ("Swirl Amount", Range(-4, 4)) = -2
        _InnerSwirlSpeed ("Inner Swirl Speed", Range(-1, 1)) = 0
        _OuterSwirlSpeed ("Outer Swirl Speed", Range(-1, 1)) = 0
        _Specular ("Specular", Range(0, 1)) = 0
        _Gloss ("Gloss", Range(0, 1)) = 0
        _VertexDetail ("Vertex Detail", Float ) = 80
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.25
        _BumpMap ("Normal", 2D) = "bump" {}
        _NormalIntensity ("Normal Intensity", Float ) = 1
        _Cubemap ("Cubemap", Cube) = "_Skybox" {}
        _Falloff ("Falloff", Range(0, 10)) = 0
        _ColourDepth ("Colour Depth", Float ) = 32
        _YScroll ("Y Scroll", Float ) = 0
        _XScroll ("X Scroll", Float ) = 0
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader {
        Tags {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
        }
        Pass {
            Name "DEFERRED"
            Tags {
                "LightMode"="Deferred"
            }
            Cull Off
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_DEFERRED
            #define SHOULD_SAMPLE_SH ( defined (LIGHTMAP_OFF) && defined(DYNAMICLIGHTMAP_OFF) )
            #define _GLOSSYENV 1
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            #include "UnityStandardBRDF.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma multi_compile ___ UNITY_HDR_ON
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            uniform samplerCUBE _Cubemap;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _Specular)
                UNITY_DEFINE_INSTANCED_PROP( float, _Gloss)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _Falloff)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float2 texcoord2 : TEXCOORD2;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 posWorld : TEXCOORD3;
                float3 normalDir : TEXCOORD4;
                float3 tangentDir : TEXCOORD5;
                float3 bitangentDir : TEXCOORD6;
                float4 projPos : TEXCOORD7;
                #if defined(LIGHTMAP_ON) || defined(UNITY_SHOULD_SAMPLE_SH)
                    float4 ambientOrLightmapUV : TEXCOORD8;
                #endif
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.uv2 = v.texcoord2;
                #ifdef LIGHTMAP_ON
                    o.ambientOrLightmapUV.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                    o.ambientOrLightmapUV.zw = 0;
                #endif
                #ifdef DYNAMICLIGHTMAP_ON
                    o.ambientOrLightmapUV.zw = v.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #endif
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                return o;
            }
            void frag(
                VertexOutput i,
                out half4 outDiffuse : SV_Target0,
                out half4 outSpecSmoothness : SV_Target1,
                out half4 outNormal : SV_Target2,
                out half4 outEmission : SV_Target3,
                float facing : VFACE )
            {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 viewReflectDirection = reflect( -viewDirection, normalDirection );
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_8950 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_8950, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
////// Lighting:
                float Pi = 3.141592654;
                float InvPi = 0.31830988618;
///////// Gloss:
                float _Gloss_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Gloss );
                float _Falloff_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Falloff );
                float4 _Cubemap_var = texCUBElod(_Cubemap,float4(reflect((-1*viewDirection),i.normalDir),_Falloff_var));
                float gloss = (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float perceptualRoughness = 1.0 - (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float roughness = perceptualRoughness * perceptualRoughness;
/////// GI Data:
                UnityLight light; // Dummy light
                light.color = 0;
                light.dir = half3(0,1,0);
                light.ndotl = max(0,dot(normalDirection,light.dir));
                UnityGIInput d;
                d.light = light;
                d.worldPos = i.posWorld.xyz;
                d.worldViewDir = viewDirection;
                d.atten = 1;
                #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                    d.ambient = 0;
                    d.lightmapUV = i.ambientOrLightmapUV;
                #else
                    d.ambient = i.ambientOrLightmapUV;
                #endif
                #if UNITY_SPECCUBE_BLENDING || UNITY_SPECCUBE_BOX_PROJECTION
                    d.boxMin[0] = unity_SpecCube0_BoxMin;
                    d.boxMin[1] = unity_SpecCube1_BoxMin;
                #endif
                #if UNITY_SPECCUBE_BOX_PROJECTION
                    d.boxMax[0] = unity_SpecCube0_BoxMax;
                    d.boxMax[1] = unity_SpecCube1_BoxMax;
                    d.probePosition[0] = unity_SpecCube0_ProbePosition;
                    d.probePosition[1] = unity_SpecCube1_ProbePosition;
                #endif
                d.probeHDR[0] = unity_SpecCube0_HDR;
                d.probeHDR[1] = unity_SpecCube1_HDR;
                Unity_GlossyEnvironmentData ugls_en_data;
                ugls_en_data.roughness = 1.0 - gloss;
                ugls_en_data.reflUVW = viewReflectDirection;
                UnityGI gi = UnityGlobalIllumination(d, 1, normalDirection, ugls_en_data );
////// Specular:
                float _Specular_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Specular );
                float3 specularColor = float3(_Specular_var,_Specular_var,_Specular_var);
                float specularMonochrome;
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 node_6219 = floor((_Diffuse_var.rgb*_Color_var.rgb*_Cubemap_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 diffuseColor = node_6219; // Need this for specular when using metallic
                diffuseColor = EnergyConservationBetweenDiffuseAndSpecular(diffuseColor, specularColor, specularMonochrome);
                specularMonochrome = 1.0-specularMonochrome;
                float NdotV = max(0.0,dot( normalDirection, viewDirection ));
                half grazingTerm = saturate( gloss + specularMonochrome );
                float3 indirectSpecular = (gi.indirect.specular);
                indirectSpecular *= FresnelLerp (specularColor, grazingTerm, NdotV);
/////// Diffuse:
                float3 indirectDiffuse = float3(0,0,0);
                indirectDiffuse += gi.indirect.diffuse;
                diffuseColor *= 1-specularMonochrome;
////// Emissive:
                float3 emissive = node_6219;
/// Final Color:
                outDiffuse = half4( diffuseColor, 1 );
                outSpecSmoothness = half4( specularColor, gloss );
                outNormal = half4( normalDirection * 0.5 + 0.5, 1 );
                outEmission = half4( node_6219, 1 );
                outEmission.rgb += indirectSpecular * 1;
                outEmission.rgb += indirectDiffuse * diffuseColor;
                #ifndef UNITY_HDR_ON
                    outEmission.rgb = exp2(-outEmission.rgb);
                #endif
            }
            ENDCG
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            Cull Off
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHOULD_SAMPLE_SH ( defined (LIGHTMAP_OFF) && defined(DYNAMICLIGHTMAP_OFF) )
            #define _GLOSSYENV 1
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            #include "UnityStandardBRDF.cginc"
            #pragma multi_compile_fwdbase_fullshadows
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            uniform samplerCUBE _Cubemap;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _Specular)
                UNITY_DEFINE_INSTANCED_PROP( float, _Gloss)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _Falloff)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float2 texcoord2 : TEXCOORD2;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 posWorld : TEXCOORD3;
                float3 normalDir : TEXCOORD4;
                float3 tangentDir : TEXCOORD5;
                float3 bitangentDir : TEXCOORD6;
                float4 projPos : TEXCOORD7;
                LIGHTING_COORDS(8,9)
                UNITY_FOG_COORDS(10)
                #if defined(LIGHTMAP_ON) || defined(UNITY_SHOULD_SAMPLE_SH)
                    float4 ambientOrLightmapUV : TEXCOORD11;
                #endif
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.uv2 = v.texcoord2;
                #ifdef LIGHTMAP_ON
                    o.ambientOrLightmapUV.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                    o.ambientOrLightmapUV.zw = 0;
                #endif
                #ifdef DYNAMICLIGHTMAP_ON
                    o.ambientOrLightmapUV.zw = v.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #endif
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                TRANSFER_VERTEX_TO_FRAGMENT(o)
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 viewReflectDirection = reflect( -viewDirection, normalDirection );
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_8950 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_8950, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColor = _LightColor0.rgb;
                float3 halfDirection = normalize(viewDirection+lightDirection);
////// Lighting:
                float attenuation = LIGHT_ATTENUATION(i);
                float3 attenColor = attenuation * _LightColor0.xyz;
                float Pi = 3.141592654;
                float InvPi = 0.31830988618;
///////// Gloss:
                float _Gloss_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Gloss );
                float _Falloff_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Falloff );
                float4 _Cubemap_var = texCUBElod(_Cubemap,float4(reflect((-1*viewDirection),i.normalDir),_Falloff_var));
                float gloss = (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float perceptualRoughness = 1.0 - (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float roughness = perceptualRoughness * perceptualRoughness;
                float specPow = exp2( gloss * 10.0 + 1.0 );
/////// GI Data:
                UnityLight light;
                #ifdef LIGHTMAP_OFF
                    light.color = lightColor;
                    light.dir = lightDirection;
                    light.ndotl = LambertTerm (normalDirection, light.dir);
                #else
                    light.color = half3(0.f, 0.f, 0.f);
                    light.ndotl = 0.0f;
                    light.dir = half3(0.f, 0.f, 0.f);
                #endif
                UnityGIInput d;
                d.light = light;
                d.worldPos = i.posWorld.xyz;
                d.worldViewDir = viewDirection;
                d.atten = attenuation;
                #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
                    d.ambient = 0;
                    d.lightmapUV = i.ambientOrLightmapUV;
                #else
                    d.ambient = i.ambientOrLightmapUV;
                #endif
                #if UNITY_SPECCUBE_BLENDING || UNITY_SPECCUBE_BOX_PROJECTION
                    d.boxMin[0] = unity_SpecCube0_BoxMin;
                    d.boxMin[1] = unity_SpecCube1_BoxMin;
                #endif
                #if UNITY_SPECCUBE_BOX_PROJECTION
                    d.boxMax[0] = unity_SpecCube0_BoxMax;
                    d.boxMax[1] = unity_SpecCube1_BoxMax;
                    d.probePosition[0] = unity_SpecCube0_ProbePosition;
                    d.probePosition[1] = unity_SpecCube1_ProbePosition;
                #endif
                d.probeHDR[0] = unity_SpecCube0_HDR;
                d.probeHDR[1] = unity_SpecCube1_HDR;
                Unity_GlossyEnvironmentData ugls_en_data;
                ugls_en_data.roughness = 1.0 - gloss;
                ugls_en_data.reflUVW = viewReflectDirection;
                UnityGI gi = UnityGlobalIllumination(d, 1, normalDirection, ugls_en_data );
                lightDirection = gi.light.dir;
                lightColor = gi.light.color;
////// Specular:
                float NdotL = saturate(dot( normalDirection, lightDirection ));
                float LdotH = saturate(dot(lightDirection, halfDirection));
                float _Specular_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Specular );
                float3 specularColor = float3(_Specular_var,_Specular_var,_Specular_var);
                float specularMonochrome;
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 node_6219 = floor((_Diffuse_var.rgb*_Color_var.rgb*_Cubemap_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 diffuseColor = node_6219; // Need this for specular when using metallic
                diffuseColor = EnergyConservationBetweenDiffuseAndSpecular(diffuseColor, specularColor, specularMonochrome);
                specularMonochrome = 1.0-specularMonochrome;
                float NdotV = abs(dot( normalDirection, viewDirection ));
                float NdotH = saturate(dot( normalDirection, halfDirection ));
                float VdotH = saturate(dot( viewDirection, halfDirection ));
                float visTerm = SmithJointGGXVisibilityTerm( NdotL, NdotV, roughness );
                float normTerm = GGXTerm(NdotH, roughness);
                float specularPBL = (visTerm*normTerm) * UNITY_PI;
                #ifdef UNITY_COLORSPACE_GAMMA
                    specularPBL = sqrt(max(1e-4h, specularPBL));
                #endif
                specularPBL = max(0, specularPBL * NdotL);
                #if defined(_SPECULARHIGHLIGHTS_OFF)
                    specularPBL = 0.0;
                #endif
                half surfaceReduction;
                #ifdef UNITY_COLORSPACE_GAMMA
                    surfaceReduction = 1.0-0.28*roughness*perceptualRoughness;
                #else
                    surfaceReduction = 1.0/(roughness*roughness + 1.0);
                #endif
                specularPBL *= any(specularColor) ? 1.0 : 0.0;
                float3 directSpecular = attenColor*specularPBL*FresnelTerm(specularColor, LdotH);
                half grazingTerm = saturate( gloss + specularMonochrome );
                float3 indirectSpecular = (gi.indirect.specular);
                indirectSpecular *= FresnelLerp (specularColor, grazingTerm, NdotV);
                indirectSpecular *= surfaceReduction;
                float3 specular = (directSpecular + indirectSpecular);
/////// Diffuse:
                NdotL = max(0.0,dot( normalDirection, lightDirection ));
                half fd90 = 0.5 + 2 * LdotH * LdotH * (1-gloss);
                float nlPow5 = Pow5(1-NdotL);
                float nvPow5 = Pow5(1-NdotV);
                float3 directDiffuse = ((1 +(fd90 - 1)*nlPow5) * (1 + (fd90 - 1)*nvPow5) * NdotL) * attenColor;
                float3 indirectDiffuse = float3(0,0,0);
                indirectDiffuse += gi.indirect.diffuse;
                diffuseColor *= 1-specularMonochrome;
                float3 diffuse = (directDiffuse + indirectDiffuse) * diffuseColor;
////// Emissive:
                float3 emissive = node_6219;
/// Final Color:
                float3 finalColor = diffuse + specular + emissive;
                fixed4 finalRGBA = fixed4(finalColor,1);
                UNITY_APPLY_FOG(i.fogCoord, finalRGBA);
                return finalRGBA;
            }
            ENDCG
        }
        Pass {
            Name "FORWARD_DELTA"
            Tags {
                "LightMode"="ForwardAdd"
            }
            Blend One One
            Cull Off
            
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHOULD_SAMPLE_SH ( defined (LIGHTMAP_OFF) && defined(DYNAMICLIGHTMAP_OFF) )
            #define _GLOSSYENV 1
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            #include "UnityStandardBRDF.cginc"
            #pragma multi_compile_fwdadd_fullshadows
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            uniform samplerCUBE _Cubemap;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _Specular)
                UNITY_DEFINE_INSTANCED_PROP( float, _Gloss)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _Falloff)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float2 texcoord2 : TEXCOORD2;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 posWorld : TEXCOORD3;
                float3 normalDir : TEXCOORD4;
                float3 tangentDir : TEXCOORD5;
                float3 bitangentDir : TEXCOORD6;
                float4 projPos : TEXCOORD7;
                LIGHTING_COORDS(8,9)
                UNITY_FOG_COORDS(10)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.uv2 = v.texcoord2;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                TRANSFER_VERTEX_TO_FRAGMENT(o)
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_8950 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_8950, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
                float3 lightDirection = normalize(lerp(_WorldSpaceLightPos0.xyz, _WorldSpaceLightPos0.xyz - i.posWorld.xyz,_WorldSpaceLightPos0.w));
                float3 lightColor = _LightColor0.rgb;
                float3 halfDirection = normalize(viewDirection+lightDirection);
////// Lighting:
                float attenuation = LIGHT_ATTENUATION(i);
                float3 attenColor = attenuation * _LightColor0.xyz;
                float Pi = 3.141592654;
                float InvPi = 0.31830988618;
///////// Gloss:
                float _Gloss_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Gloss );
                float _Falloff_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Falloff );
                float4 _Cubemap_var = texCUBElod(_Cubemap,float4(reflect((-1*viewDirection),i.normalDir),_Falloff_var));
                float gloss = (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float perceptualRoughness = 1.0 - (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                float roughness = perceptualRoughness * perceptualRoughness;
                float specPow = exp2( gloss * 10.0 + 1.0 );
////// Specular:
                float NdotL = saturate(dot( normalDirection, lightDirection ));
                float LdotH = saturate(dot(lightDirection, halfDirection));
                float _Specular_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Specular );
                float3 specularColor = float3(_Specular_var,_Specular_var,_Specular_var);
                float specularMonochrome;
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 node_6219 = floor((_Diffuse_var.rgb*_Color_var.rgb*_Cubemap_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 diffuseColor = node_6219; // Need this for specular when using metallic
                diffuseColor = EnergyConservationBetweenDiffuseAndSpecular(diffuseColor, specularColor, specularMonochrome);
                specularMonochrome = 1.0-specularMonochrome;
                float NdotV = abs(dot( normalDirection, viewDirection ));
                float NdotH = saturate(dot( normalDirection, halfDirection ));
                float VdotH = saturate(dot( viewDirection, halfDirection ));
                float visTerm = SmithJointGGXVisibilityTerm( NdotL, NdotV, roughness );
                float normTerm = GGXTerm(NdotH, roughness);
                float specularPBL = (visTerm*normTerm) * UNITY_PI;
                #ifdef UNITY_COLORSPACE_GAMMA
                    specularPBL = sqrt(max(1e-4h, specularPBL));
                #endif
                specularPBL = max(0, specularPBL * NdotL);
                #if defined(_SPECULARHIGHLIGHTS_OFF)
                    specularPBL = 0.0;
                #endif
                specularPBL *= any(specularColor) ? 1.0 : 0.0;
                float3 directSpecular = attenColor*specularPBL*FresnelTerm(specularColor, LdotH);
                float3 specular = directSpecular;
/////// Diffuse:
                NdotL = max(0.0,dot( normalDirection, lightDirection ));
                half fd90 = 0.5 + 2 * LdotH * LdotH * (1-gloss);
                float nlPow5 = Pow5(1-NdotL);
                float nvPow5 = Pow5(1-NdotV);
                float3 directDiffuse = ((1 +(fd90 - 1)*nlPow5) * (1 + (fd90 - 1)*nvPow5) * NdotL) * attenColor;
                diffuseColor *= 1-specularMonochrome;
                float3 diffuse = directDiffuse * diffuseColor;
/// Final Color:
                float3 finalColor = diffuse + specular;
                fixed4 finalRGBA = fixed4(finalColor * 1,0);
                UNITY_APPLY_FOG(i.fogCoord, finalRGBA);
                return finalRGBA;
            }
            ENDCG
        }
        Pass {
            Name "ShadowCaster"
            Tags {
                "LightMode"="ShadowCaster"
            }
            Offset 1, 1
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SHOULD_SAMPLE_SH ( defined (LIGHTMAP_OFF) && defined(DYNAMICLIGHTMAP_OFF) )
            #define _GLOSSYENV 1
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            #include "UnityStandardBRDF.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float2 texcoord2 : TEXCOORD2;
            };
            struct VertexOutput {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD1;
                float2 uv1 : TEXCOORD2;
                float2 uv2 : TEXCOORD3;
                float4 posWorld : TEXCOORD4;
                float4 projPos : TEXCOORD5;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.uv2 = v.texcoord2;
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_8950 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_8950, _Diffuse));
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                clip( BinaryDither2x2(pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)) - 1.5, sceneUVs) );
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
        Pass {
            Name "Meta"
            Tags {
                "LightMode"="Meta"
            }
            Cull Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define UNITY_PASS_META 1
            #define SHOULD_SAMPLE_SH ( defined (LIGHTMAP_OFF) && defined(DYNAMICLIGHTMAP_OFF) )
            #define _GLOSSYENV 1
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            #include "UnityStandardBRDF.cginc"
            #include "UnityMetaPass.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma multi_compile LIGHTMAP_OFF LIGHTMAP_ON
            #pragma multi_compile DIRLIGHTMAP_OFF DIRLIGHTMAP_COMBINED DIRLIGHTMAP_SEPARATE
            #pragma multi_compile DYNAMICLIGHTMAP_OFF DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma target 3.0
            // Dithering function, to use with scene UVs (screen pixel coords)
            // 2x2 Bayer matrix, based on https://en.wikipedia.org/wiki/Ordered_dithering
            float BinaryDither2x2( float value, float2 sceneUVs ) {
                float2x2 mtx = float2x2(
                    float2( 1, 3 )/5.0,
                    float2( 4, 2 )/5.0
                );
                float2 px = floor(_ScreenParams.xy * sceneUVs);
                int xSmp = fmod(px.x,2);
                int ySmp = fmod(px.y,2);
                float2 xVec = 1-saturate(abs(float2(0,1) - xSmp));
                float2 yVec = 1-saturate(abs(float2(0,1) - ySmp));
                float2 pxMult = float2( dot(mtx[0],yVec), dot(mtx[1],yVec) );
                return round(value + dot(pxMult, xVec));
            }
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform samplerCUBE _Cubemap;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _SwirlAmount)
                UNITY_DEFINE_INSTANCED_PROP( float, _OuterSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _InnerSwirlSpeed)
                UNITY_DEFINE_INSTANCED_PROP( float, _Specular)
                UNITY_DEFINE_INSTANCED_PROP( float, _Gloss)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _Falloff)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
                UNITY_DEFINE_INSTANCED_PROP( float, _YScroll)
                UNITY_DEFINE_INSTANCED_PROP( float, _XScroll)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord0 : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
                float2 texcoord2 : TEXCOORD2;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float4 posWorld : TEXCOORD3;
                float3 normalDir : TEXCOORD4;
                float4 projPos : TEXCOORD5;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.uv1 = v.texcoord1;
                o.uv2 = v.texcoord2;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += mul( float4(((round(node_1173)-node_1173)/_VertexDetail_var),0), UNITY_MATRIX_V ).xyz.rgb;
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityMetaVertexPosition(v.vertex, v.texcoord1.xy, v.texcoord2.xy, unity_LightmapST, unity_DynamicLightmapST );
                o.projPos = ComputeScreenPos (o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
                return o;
            }
            float4 frag(VertexOutput i, float facing : VFACE) : SV_Target {
                UNITY_SETUP_INSTANCE_ID( i );
                float isFrontFace = ( facing >= 0 ? 1 : 0 );
                float faceSign = ( facing >= 0 ? 1 : -1 );
                i.normalDir = normalize(i.normalDir);
                i.normalDir *= faceSign;
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 normalDirection = i.normalDir;
                float2 sceneUVs = (i.projPos.xy / i.projPos.w);
                UnityMetaInput o;
                UNITY_INITIALIZE_OUTPUT( UnityMetaInput, o );
                
                float node_1271 = 0.5;
                float _SwirlAmount_var = UNITY_ACCESS_INSTANCED_PROP( Props, _SwirlAmount );
                float _InnerSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _InnerSwirlSpeed ); // Inner Swirl Sine Wave
                float4 node_5790 = _Time;
                float _OuterSwirlSpeed_var = UNITY_ACCESS_INSTANCED_PROP( Props, _OuterSwirlSpeed );
                float node_6623_ang = ((saturate((1.0 - length(abs(((i.uv0-node_1271)/node_1271)))))*_SwirlAmount_var*(sin((_InnerSwirlSpeed_var*node_5790.g))*0.5+0.5))+(frac((node_5790.g*(_OuterSwirlSpeed_var*0.09999999+-0.000000000224)))*6.28318530718));
                float node_6623_spd = 1.0;
                float node_6623_cos = cos(node_6623_spd*node_6623_ang);
                float node_6623_sin = sin(node_6623_spd*node_6623_ang);
                float2 node_6623_piv = float2(0.5,0.5);
                float2 node_6623 = (mul(i.uv0-node_6623_piv,float2x2( node_6623_cos, -node_6623_sin, node_6623_sin, node_6623_cos))+node_6623_piv);
                float _XScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _XScroll );
                float _YScroll_var = UNITY_ACCESS_INSTANCED_PROP( Props, _YScroll );
                float2 node_8950 = (node_6623+(i.uv0+(float2(_XScroll_var,_YScroll_var)*node_5790.g)));
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(node_8950, _Diffuse));
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _Falloff_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Falloff );
                float4 _Cubemap_var = texCUBElod(_Cubemap,float4(reflect((-1*viewDirection),i.normalDir),_Falloff_var));
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 node_6219 = floor((_Diffuse_var.rgb*_Color_var.rgb*_Cubemap_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                o.Emission = node_6219;
                
                float3 diffColor = node_6219;
                float _Specular_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Specular );
                float3 specColor = float3(_Specular_var,_Specular_var,_Specular_var);
                float specularMonochrome = max(max(specColor.r, specColor.g),specColor.b);
                diffColor *= (1.0-specularMonochrome);
                float _Gloss_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Gloss );
                float roughness = 1.0 - (_Gloss_var*_Diffuse_var.a*_Cubemap_var.a);
                o.Albedo = diffColor + specColor * roughness * roughness * 0.5;
                
                return UnityMetaFragment( o );
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
///    CustomEditor "ShaderForgeMaterialInspector"
}
