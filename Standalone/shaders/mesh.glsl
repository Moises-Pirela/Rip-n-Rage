// Default lit mesh shader (engine built-in "mesh"). Editing this file changes every mesh live.
// Engine uniforms: uViewProj, uModel, uTime, uTimeVec, uTex (unit 0), uTint, uUVScale, uEmissive + lighting.glsl set.
#pragma vertex
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aUV;
layout(location = 3) in vec4 aColor;   // vertex paint (white when unpainted)
uniform mat4 uViewProj;
uniform mat4 uModel;
out vec3 vNormal;
out vec2 vUV;
out vec3 vWorldPos;
out vec4 vColor;
void main() {
    vec4 wp = uModel * vec4(aPos, 1.0);
    vWorldPos = wp.xyz;
    vNormal = mat3(uModel) * aNormal;
    vUV = aUV;
    vColor = aColor;
    gl_Position = uViewProj * wp;
}

#pragma fragment
#version 330 core
in vec3 vNormal;
in vec2 vUV;
in vec3 vWorldPos;
in vec4 vColor;
uniform sampler2D uTex;
uniform vec4 uTint;
uniform vec2 uUVScale;
uniform float uEmissive;
uniform int uDebugShadow;
#include "lighting.glsl"
out vec4 FragColor;
void main() {
    vec3 n = normalize(vNormal);
    if (uDebugShadow == 1) { FragColor = vec4(vec3(sampleShadow(vWorldPos, n)), 1.0); return; }
    vec4 albedo = texture(uTex, vUV * uUVScale) * uTint * vColor;
    vec3 lit = shade(albedo.rgb, n, vWorldPos, 0.0) + albedo.rgb * uEmissive;
    FragColor = vec4(applyFog(lit, vWorldPos), albedo.a);
}
