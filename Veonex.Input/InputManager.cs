using SDL;

namespace Veonex.Input;

public sealed unsafe class InputManager
{
	private readonly HashSet<SDL_Scancode> _down = [];
	private readonly HashSet<SDL_Scancode> _pressed = [];
	private readonly HashSet<SDL_Scancode> _released = [];

	public bool QuitRequested { get; private set; }

	public bool WindowSizeChanged { get; private set; }

	public int WindowWidth { get; private set; }

	public int WindowHeight { get; private set; }

	public void Update()
	{
		_pressed.Clear();
		_released.Clear();

		WindowSizeChanged = false;

		SDL_Event ev;

		while (SDL3.SDL_PollEvent(&ev))
		{
			switch (ev.Type)
			{
				case SDL_EventType.SDL_EVENT_QUIT:
					QuitRequested = true;
					break;

				case SDL_EventType.SDL_EVENT_KEY_DOWN:
					HandleKeyDown(ev.key);
					break;

				case SDL_EventType.SDL_EVENT_KEY_UP:
					HandleKeyUp(ev.key);
					break;

				case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
					WindowWidth = ev.window.data1;
					WindowHeight = ev.window.data2;
					WindowSizeChanged = true;
					break;

				case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
					ClearKeyboardState();
					break;
			}
		}
	}

	public bool IsKeyDown(SDL_Scancode key) =>
		_down.Contains(key);

	public bool IsKeyPressed(SDL_Scancode key) =>
		_pressed.Contains(key);

	public bool IsKeyReleased(SDL_Scancode key) =>
		_released.Contains(key);

	private void HandleKeyDown(
		SDL_KeyboardEvent keyEvent)
	{
		if (keyEvent.repeat)
			return;

		if (_down.Add(keyEvent.scancode))
		{
			_pressed.Add(keyEvent.scancode);
		}
	}

	private void HandleKeyUp(
		SDL_KeyboardEvent keyEvent)
	{
		if (_down.Remove(keyEvent.scancode))
		{
			_released.Add(keyEvent.scancode);
		}
	}

	private void ClearKeyboardState()
	{
		foreach (SDL_Scancode key in _down)
		{
			_released.Add(key);
		}

		_down.Clear();
	}
}