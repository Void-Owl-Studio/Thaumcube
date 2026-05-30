#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;
layout(location = 3) in uint inBlockType;
layout(location = 4) in vec3 inTint;
layout(location = 5) in float inAlpha;

layout(set = 0, binding = 0) uniform CameraUniform
{
    mat4 view;
    mat4 projection;
    vec4 cameraPosition;
    vec4 fogColor;
    vec4 skyLightColor;
    vec4 fogSettings;
    vec4 lightDirection;
    vec4 cloudColor;
    vec4 cloudMotion;
    vec4 cloudOffset;
} camera;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec2 fragUv;
layout(location = 2) flat out uint fragBlockType;
layout(location = 3) out vec3 fragTint;
layout(location = 4) out vec3 fragWorldPosition;
layout(location = 5) out float fragAlpha;

void main()
{
    vec3 worldPosition = inPosition;
    if (inBlockType == 200u)
    {
        worldPosition.xz += camera.cloudOffset.xy;
    }

    gl_Position = camera.projection * camera.view * vec4(worldPosition, 1.0);
    fragNormal = inNormal;
    fragUv = inUv;
    fragBlockType = inBlockType;
    fragTint = inTint;
    fragWorldPosition = worldPosition;
    fragAlpha = inAlpha;
}
