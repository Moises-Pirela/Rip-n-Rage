// Procedural sky gradient (engine built-in "sky"). Uniforms: uInvViewProj, uHorizon, uZenith, uGround, uSunDir, uTime.
#pragma vertex
#version 330 core
layout(location = 0) in vec2 aPos;
out vec2 vPos;
void main() { vPos = aPos; gl_Position = vec4(aPos, 0.999999, 1.0); }

#pragma fragment
#version 330 core
in vec2 vPos;
uniform mat4 uInvViewProj;
uniform vec3 uHorizon;
uniform vec3 uZenith;
uniform vec3 uGround;
uniform vec3 uSunDir;
out vec4 FragColor;
void main() {
    vec4 near = uInvViewProj * vec4(vPos, -1.0, 1.0);
    vec4 far = uInvViewProj * vec4(vPos, 1.0, 1.0);
    vec3 dir = normalize(far.xyz / far.w - near.xyz / near.w);
    float t = clamp(dir.y, -1.0, 1.0);
    vec3 col = t >= 0.0 ? mix(uHorizon, uZenith, pow(t, 0.6)) : mix(uHorizon, uGround, pow(-t, 0.5));
    float sun = pow(max(dot(dir, -normalize(uSunDir)), 0.0), 256.0);
    col += vec3(1.0, 0.95, 0.8) * sun * 0.8;
    FragColor = vec4(col, 1.0);
}
