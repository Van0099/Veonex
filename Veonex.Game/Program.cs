using Veonex.Core;
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

Scene scene = new();

Entity cameraEntity =
    scene.Add("camera");

cameraEntity.Add<Transform>();
cameraEntity.Add<Camera>();

Camera camera =
    cameraEntity.Get<Camera>();

while (renderer.IsRunning)
{
    renderer.RenderFrame(
        scene,
        camera);
}