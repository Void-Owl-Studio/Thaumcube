#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inUv;
layout(location = 3) in uint inBlockType;
layout(location = 4) in vec3 inTint;

layout(set = 0, binding = 0) uniform CameraUniform
{
    mat4 view;
    mat4 projection;
} camera;

layout(location = 0) out vec3 fragNormal;
layout(location = 1) out vec2 fragUv;
layout(location = 2) flat out uint fragBlockType;
layout(location = 3) out vec3 fragTint;

void main()
{
    gl_Position = camera.projection * camera.view * vec4(inPosition, 1.0);
    fragNormal = inNormal;
    fragUv = inUv;
    fragBlockType = inBlockType;
    fragTint = inTint;
}
