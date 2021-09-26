// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Shader created with Shader Forge v1.40 
// Shader Forge (c) Freya Holmer - http://www.acegikmo.com/shaderforge/
// Note: Manually altering this data may prevent you from opening it in Shader Forge
/*SF_DATA;ver:1.40;sub:START;pass:START;ps:flbk:,iptp:0,cusa:False,bamd:0,cgin:,cpap:True,lico:1,lgpr:1,limd:1,spmd:1,trmd:0,grmd:0,uamb:True,mssp:True,bkdf:False,hqlp:False,rprd:False,enco:False,rmgx:True,imps:True,rpth:0,vtps:0,hqsc:True,nrmq:1,nrsp:0,vomd:0,spxs:False,tesm:0,olmd:1,culm:0,bsrc:3,bdst:7,dpts:2,wrdp:False,dith:0,atcv:False,rfrpo:True,rfrpn:Refraction,coma:15,ufog:True,aust:True,igpj:True,qofs:0,qpre:3,rntp:2,fgom:False,fgoc:False,fgod:False,fgor:False,fgmd:0,fgcr:0.5,fgcg:0.5,fgcb:0.5,fgca:1,fgde:0.01,fgrn:0,fgrf:300,stcl:False,atwp:False,stva:128,stmr:255,stmw:255,stcp:6,stps:0,stfa:0,stfz:0,ofsf:0,ofsu:0,f2p0:False,fnsp:False,fnfb:False,fsmp:False;n:type:ShaderForge.SFN_Final,id:4013,x:32877,y:32662,varname:node_4013,prsc:2|diff-4456-OUT,normal-8068-OUT,alpha-7424-OUT,voffset-414-OUT;n:type:ShaderForge.SFN_Color,id:1304,x:32174,y:32775,ptovrint:False,ptlb:Color,ptin:_Color,varname:node_1304,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,c1:1,c2:1,c3:1,c4:1;n:type:ShaderForge.SFN_FragmentPosition,id:5440,x:31426,y:32983,varname:node_5440,prsc:2;n:type:ShaderForge.SFN_Transform,id:3472,x:31635,y:32983,varname:node_3472,prsc:2,tffrom:0,tfto:3|IN-5440-XYZ;n:type:ShaderForge.SFN_Multiply,id:1173,x:31816,y:33072,varname:node_1173,prsc:2|A-3472-XYZ,B-3751-OUT;n:type:ShaderForge.SFN_Round,id:5805,x:31981,y:33072,varname:node_5805,prsc:2|IN-1173-OUT;n:type:ShaderForge.SFN_Subtract,id:6209,x:32165,y:33072,varname:node_6209,prsc:2|A-5805-OUT,B-1173-OUT;n:type:ShaderForge.SFN_Divide,id:414,x:32335,y:33072,varname:node_414,prsc:2|A-6209-OUT,B-3751-OUT;n:type:ShaderForge.SFN_Tex2d,id:109,x:32174,y:32576,ptovrint:False,ptlb:Diffuse,ptin:_Diffuse,varname:node_109,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:0,isnm:False;n:type:ShaderForge.SFN_Multiply,id:1980,x:32383,y:32700,varname:node_1980,prsc:2|A-109-RGB,B-1304-RGB;n:type:ShaderForge.SFN_Slider,id:5256,x:31448,y:32790,ptovrint:False,ptlb:Alpha Clip,ptin:_AlphaClip,varname:_AlphaClip_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,min:0,cur:0.25,max:1;n:type:ShaderForge.SFN_RemapRange,id:158,x:31787,y:32764,varname:node_158,prsc:2,frmn:0,frmx:1,tomn:0,tomx:4|IN-5256-OUT;n:type:ShaderForge.SFN_Power,id:7424,x:31978,y:32764,varname:node_7424,prsc:2|VAL-109-A,EXP-158-OUT;n:type:ShaderForge.SFN_ValueProperty,id:9342,x:31614,y:32530,ptovrint:False,ptlb:Normal Intensity,ptin:_NormalIntensity,varname:node_7251,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:1;n:type:ShaderForge.SFN_Vector3,id:8324,x:31795,y:32436,varname:node_8324,prsc:2,v1:0,v2:0,v3:1;n:type:ShaderForge.SFN_OneMinus,id:8961,x:31795,y:32530,varname:node_8961,prsc:2|IN-9342-OUT;n:type:ShaderForge.SFN_Lerp,id:8068,x:32174,y:32382,varname:node_8068,prsc:2|A-3392-RGB,B-8324-OUT,T-8961-OUT;n:type:ShaderForge.SFN_Tex2d,id:3392,x:31795,y:32264,ptovrint:True,ptlb:Normal,ptin:_BumpMap,varname:_BumpMap,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,ntxv:3,isnm:True;n:type:ShaderForge.SFN_ValueProperty,id:3751,x:31635,y:33182,ptovrint:False,ptlb:Vertex Detail,ptin:_VertexDetail,varname:node_3751,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:80;n:type:ShaderForge.SFN_Posterize,id:4456,x:32603,y:32700,varname:node_4456,prsc:2|IN-1980-OUT,STPS-5631-OUT;n:type:ShaderForge.SFN_ValueProperty,id:5631,x:32383,y:32879,ptovrint:False,ptlb:Colour Depth,ptin:_ColourDepth,varname:node_5631,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:32;n:type:ShaderForge.SFN_ValueProperty,id:8057,x:31316,y:32690,ptovrint:False,ptlb:Y Scroll_copy,ptin:_YScroll_copy,varname:_YScroll_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_ValueProperty,id:9432,x:31316,y:32590,ptovrint:False,ptlb:X Scroll_copy,ptin:_XScroll_copy,varname:_XScroll_copy,prsc:2,glob:False,taghide:False,taghdr:False,tagprd:False,tagnsco:False,tagnrm:False,v1:0;n:type:ShaderForge.SFN_Append,id:6235,x:31525,y:32618,varname:node_6235,prsc:2|A-9432-OUT,B-8057-OUT;n:type:ShaderForge.SFN_Multiply,id:7160,x:31514,y:32380,varname:node_7160,prsc:2|A-6235-OUT;n:type:ShaderForge.SFN_Add,id:6896,x:31514,y:32173,varname:node_6896,prsc:2|B-7160-OUT;proporder:109-1304-3751-5256-3392-9342-5631;pass:END;sub:END;*/

Shader "Retro3D/Non-Dithered/Retro3D_ND" {
    Properties {
        _Diffuse ("Diffuse", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _VertexDetail ("Vertex Detail", Float ) = 80
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.25
        _BumpMap ("Normal", 2D) = "bump" {}
        _NormalIntensity ("Normal Intensity", Float ) = 1
        _ColourDepth ("Colour Depth", Float ) = 32
        [HideInInspector]_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader {
        Tags {
            "IgnoreProjector"="True"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        Pass {
            Name "FORWARD"
            Tags {
                "LightMode"="ForwardBase"
            }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma target 3.0
            uniform float4 _LightColor0;
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 tangentDir : TEXCOORD3;
                float3 bitangentDir : TEXCOORD4;
                UNITY_FOG_COORDS(5)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += ((round(node_1173)-node_1173)/_VertexDetail_var);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                i.normalDir = normalize(i.normalDir);
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 lightDirection = normalize(_WorldSpaceLightPos0.xyz);
                float3 lightColor = _LightColor0.rgb;
////// Lighting:
                float attenuation = 1;
                float3 attenColor = attenuation * _LightColor0.xyz;
/////// Diffuse:
                float NdotL = max(0.0,dot( normalDirection, lightDirection ));
                float3 directDiffuse = max( 0.0, NdotL) * attenColor;
                float3 indirectDiffuse = float3(0,0,0);
                indirectDiffuse += UNITY_LIGHTMODEL_AMBIENT.rgb; // Ambient Light
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(i.uv0, _Diffuse));
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 diffuseColor = floor((_Diffuse_var.rgb*_Color_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 diffuse = (directDiffuse + indirectDiffuse) * diffuseColor;
/// Final Color:
                float3 finalColor = diffuse;
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                fixed4 finalRGBA = fixed4(finalColor,pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)));
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
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            #pragma multi_compile_fwdadd
            #pragma multi_compile_fog
            #pragma target 3.0
            uniform float4 _LightColor0;
            uniform sampler2D _Diffuse; uniform float4 _Diffuse_ST;
            uniform sampler2D _BumpMap; uniform float4 _BumpMap_ST;
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP( float, _AlphaClip)
                UNITY_DEFINE_INSTANCED_PROP( float, _NormalIntensity)
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
                UNITY_DEFINE_INSTANCED_PROP( float, _ColourDepth)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 texcoord0 : TEXCOORD0;
            };
            struct VertexOutput {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float2 uv0 : TEXCOORD0;
                float4 posWorld : TEXCOORD1;
                float3 normalDir : TEXCOORD2;
                float3 tangentDir : TEXCOORD3;
                float3 bitangentDir : TEXCOORD4;
                LIGHTING_COORDS(5,6)
                UNITY_FOG_COORDS(7)
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                o.uv0 = v.texcoord0;
                o.normalDir = UnityObjectToWorldNormal(v.normal);
                o.tangentDir = normalize( mul( unity_ObjectToWorld, float4( v.tangent.xyz, 0.0 ) ).xyz );
                o.bitangentDir = normalize(cross(o.normalDir, o.tangentDir) * v.tangent.w);
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += ((round(node_1173)-node_1173)/_VertexDetail_var);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                float3 lightColor = _LightColor0.rgb;
                o.pos = UnityObjectToClipPos( v.vertex );
                UNITY_TRANSFER_FOG(o,o.pos);
                TRANSFER_VERTEX_TO_FRAGMENT(o)
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                i.normalDir = normalize(i.normalDir);
                float3x3 tangentTransform = float3x3( i.tangentDir, i.bitangentDir, i.normalDir);
                float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.posWorld.xyz);
                float3 _BumpMap_var = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(i.uv0, _BumpMap)));
                float _NormalIntensity_var = UNITY_ACCESS_INSTANCED_PROP( Props, _NormalIntensity );
                float3 normalLocal = lerp(_BumpMap_var.rgb,float3(0,0,1),(1.0 - _NormalIntensity_var));
                float3 normalDirection = normalize(mul( normalLocal, tangentTransform )); // Perturbed normals
                float3 lightDirection = normalize(lerp(_WorldSpaceLightPos0.xyz, _WorldSpaceLightPos0.xyz - i.posWorld.xyz,_WorldSpaceLightPos0.w));
                float3 lightColor = _LightColor0.rgb;
////// Lighting:
                float attenuation = LIGHT_ATTENUATION(i);
                float3 attenColor = attenuation * _LightColor0.xyz;
/////// Diffuse:
                float NdotL = max(0.0,dot( normalDirection, lightDirection ));
                float3 directDiffuse = max( 0.0, NdotL) * attenColor;
                float4 _Diffuse_var = tex2D(_Diffuse,TRANSFORM_TEX(i.uv0, _Diffuse));
                float4 _Color_var = UNITY_ACCESS_INSTANCED_PROP( Props, _Color );
                float _ColourDepth_var = UNITY_ACCESS_INSTANCED_PROP( Props, _ColourDepth );
                float3 diffuseColor = floor((_Diffuse_var.rgb*_Color_var.rgb) * _ColourDepth_var) / (_ColourDepth_var - 1);
                float3 diffuse = directDiffuse * diffuseColor;
/// Final Color:
                float3 finalColor = diffuse;
                float _AlphaClip_var = UNITY_ACCESS_INSTANCED_PROP( Props, _AlphaClip );
                fixed4 finalRGBA = fixed4(finalColor * pow(_Diffuse_var.a,(_AlphaClip_var*4.0+0.0)),0);
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
            Cull Back
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_fog
            #pragma target 3.0
            UNITY_INSTANCING_BUFFER_START( Props )
                UNITY_DEFINE_INSTANCED_PROP( float, _VertexDetail)
            UNITY_INSTANCING_BUFFER_END( Props )
            struct VertexInput {
                float4 vertex : POSITION;
            };
            struct VertexOutput {
                V2F_SHADOW_CASTER;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 posWorld : TEXCOORD1;
            };
            VertexOutput vert (VertexInput v) {
                VertexOutput o = (VertexOutput)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                float _VertexDetail_var = UNITY_ACCESS_INSTANCED_PROP( Props, _VertexDetail );
                float3 node_1173 = (mul( UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).rgb,0) ).xyz.rgb*_VertexDetail_var);
                v.vertex.xyz += ((round(node_1173)-node_1173)/_VertexDetail_var);
                o.posWorld = mul(unity_ObjectToWorld, v.vertex);
                o.pos = UnityObjectToClipPos( v.vertex );
                TRANSFER_SHADOW_CASTER(o)
                return o;
            }
            float4 frag(VertexOutput i) : COLOR {
                UNITY_SETUP_INSTANCE_ID( i );
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
///    CustomEditor "ShaderForgeMaterialInspector"
}
