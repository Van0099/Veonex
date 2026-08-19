using Veonex.Core;
using Veonex.Game;
using Veonex.Mathematics;
using Veonex.Render;

WindowParameters parameters = new()
{
    Width = 1280,
    Height = 720,
    Title = "Veonex",
    VSync = false,
    Resizable = true
};

using RenderBackend renderer =
    new(parameters);


// ============================================================
// Scene
// ============================================================

Scene scene = new();


// ============================================================
// Camera
// ============================================================

Entity cameraEntity =
    scene.Add("Camera");

Transform cameraTransform =
    cameraEntity.Add<Transform>();

Camera camera =
    cameraEntity.Add<Camera>();

camera.AspectRatio =
    (double)parameters.Width /
    parameters.Height;


// Camera position

cameraTransform.Position =
    new DVector3(
        0,
        0,
        8);


// Camera looks towards the origin

DVector3 target =
    DVector3.Zero;

DVector3 direction =
    target -
    cameraTransform.Position;

direction =
    direction.Normalized();

double horizontalLength =
    Math.Sqrt(
        direction.X * direction.X +
        direction.Z * direction.Z);

double yaw =
    Math.Atan2(
        direction.X,
        direction.Z);

double pitch =
    Math.Atan2(
        -direction.Y,
        horizontalLength);

cameraTransform.Rotation =
    new DVector3(
        pitch * 180.0 / Math.PI,
        yaw * 180.0 / Math.PI,
        0.0);

Mesh cubeMesh =
    CubeMeshTemplate.Create();

Entity cube1 =
    scene.Add("Cube_1");

Transform cube1Transform =
    cube1.Add<Transform>();

cube1Transform.Position =
    new DVector3(
        -2,
        0,
        0);

MeshRenderer cube1Renderer =
    cube1.Add<MeshRenderer>();

cube1Renderer.Mesh =
    cubeMesh;

Entity cube2 =
    scene.Add("Cube_2");

Transform cube2Transform =
    cube2.Add<Transform>();

cube2Transform.Position =
    new DVector3(
        2,
        0,
        0);

MeshRenderer cube2Renderer =
    cube2.Add<MeshRenderer>();

cube2Renderer.Mesh =
    cubeMesh;


while (renderer.IsRunning)
{
    renderer.RenderFrame(
        scene,
        camera);
}