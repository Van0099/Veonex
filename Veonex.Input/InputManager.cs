using SDL;

namespace Veonex.Input;

public sealed unsafe class InputManager
{
	private readonly HashSet<SDL_Scancode> _down = [];
	private readonly HashSet<SDL_Scancode> _pressed = [];
	private readonly HashSet<SDL_Scancode> _released = [];

	private readonly HashSet<byte> _mouseDown = [];
	private readonly HashSet<byte> _mousePressed = [];
	private readonly HashSet<byte> _mouseReleased = [];

	public bool QuitRequested { get; private set; }

	public bool WindowSizeChanged { get; private set; }

	public int WindowWidth { get; private set; }

	public int WindowHeight { get; private set; }

	public float MouseDeltaX { get; private set; }

	public float MouseDeltaY { get; private set; }

	public void Update()
	{
		_pressed.Clear();
		_released.Clear();

		_mousePressed.Clear();
		_mouseReleased.Clear();

		MouseDeltaX = 0.0f;
		MouseDeltaY = 0.0f;

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

				case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
					MouseDeltaX += ev.motion.xrel;
					MouseDeltaY += ev.motion.yrel;
					break;

				case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
					HandleMouseButtonDown(ev.button);
					break;

				case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
					HandleMouseButtonUp(ev.button);
					break;

				case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
					WindowWidth = ev.window.data1;
					WindowHeight = ev.window.data2;
					WindowSizeChanged = true;
					break;

				case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
					ClearKeyboardState();
					ClearMouseState();
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

	public bool IsMouseButtonDown(byte button) =>
		_mouseDown.Contains(button);

	public bool IsMouseButtonPressed(byte button) =>
		_mousePressed.Contains(button);

	public bool IsMouseButtonReleased(byte button) =>
		_mouseReleased.Contains(button);

	private void HandleKeyDown(SDL_KeyboardEvent keyEvent)
	{
		if (keyEvent.repeat)
			return;

		if (_down.Add(keyEvent.scancode))
		{
			_pressed.Add(keyEvent.scancode);
		}
	}

	private void HandleKeyUp(SDL_KeyboardEvent keyEvent)
	{
		if (_down.Remove(keyEvent.scancode))
		{
			_released.Add(keyEvent.scancode);
		}
	}

	private void HandleMouseButtonDown(
		SDL_MouseButtonEvent buttonEvent)
	{
		if (_mouseDown.Add(buttonEvent.button))
		{
			_mousePressed.Add(buttonEvent.button);
		}
	}

	private void HandleMouseButtonUp(
		SDL_MouseButtonEvent buttonEvent)
	{
		if (_mouseDown.Remove(buttonEvent.button))
		{
			_mouseReleased.Add(buttonEvent.button);
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

	private void ClearMouseState()
	{
		foreach (byte button in _mouseDown)
		{
			_mouseReleased.Add(button);
		}

		_mouseDown.Clear();
	}
}