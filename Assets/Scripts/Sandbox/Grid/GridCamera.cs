using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Glitchers.EcoKnow.Sandbox.Grid
{
    public enum CameraMoveDir
    {
        UP,
        DOWN,
        LEFT,
        RIGHT
    }

    public enum CameraZoom
    {
        INZOOM,
        OUTZOOM
    }


    public class GridCamera : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera _camera;
        public Camera Camera => _camera;

        [SerializeField] private float _zPlane = -10f;

        private GridManager _gridManager;


        [Header("Controls")]
        // Grid visual extent in world space (viewport is clamped to these).
        private float _gridLeft, _gridRight, _gridTop, _gridBottom;
        private Vector2 _movementUnits;

        [SerializeField] float _movementIntervalDuration;
        private float _movementIntervalTimer;

        [Header("Mouse/Touch Controls")]
        private bool _isDragging = false;
        private bool _leftMouseStartedOverUI = false;
        private Vector3 _lastMousePosition;
        private Vector2 _touchStartPosition;
        // Finger travel (pixels) before a one-finger touch becomes a pan rather than a tap.
        private const float TouchDragThreshold = 10f;
        [SerializeField] private float _mouseSensitivity = 1.0f;

        [Header("Pixel Perfect")]
        [SerializeField] private int _assetsPPU = 32;
        // 640x360 reference resolution — divides evenly into 720p (2x), 1080p (3x), 1440p (4x), 4K (6x).
        private const int BaseRefResolutionY = 360;
        private float _baseOrthoSize;

        [Header("Zoom")]
        [SerializeField] private int _zoomSteps = 24;
        [Tooltip("Max zoom-in: minimum tiles visible vertically.")]
        [SerializeField] private float _minTilesVisible = 3f;
        [Tooltip("Buffer tiles added around populated content for default zoom.")]
        [SerializeField] private float _contentBuffer = 2f;
        [Tooltip("Max zoom-out multiplier relative to default (content-fit) zoom.")]
        [SerializeField] private float _maxZoomOutMultiplier = 3f;
        private float[] _zoomLevels;
        private int _currentZoomIndex;
        private float _currentZoomLevel = 1.0f;
        private float _pinchZoomAccumulator;

        [Header("Bounds")]
        [Tooltip("If true, the camera is clamped to the grid extent. Disable to allow panning past the edge of the world.")]
        [SerializeField] private bool _boundsClampingEnabled = false;

        private const string LogChannel = "[GridCamera]";

        public void Init(GridManager gridManager)
        {
            _gridManager = gridManager;

            // Remove any stale PixelPerfectCamera that could override orthographicSize
            Component ppc = _camera.GetComponent("PixelPerfectCamera");
            if (ppc != null)
            {
                Debug.LogWarning($"{LogChannel} Removing stale PixelPerfectCamera from camera");
                DestroyImmediate(ppc);
            }

            // Pixel-perfect base ortho: refResY / (2 * PPU)
            int ppu = Mathf.Max(1, _assetsPPU);
            _baseOrthoSize = BaseRefResolutionY / (2f * ppu);
            _camera.allowMSAA = false;

            ComputeZoomLevels();

            CalculateBounds();
            CalculateMovementUnits();
            CenterCamera();
            ApplyZoom();
        }

        public void UpdateInput()
        {
            HandleKeyboardInput();
            HandleMouseInput();
            HandleTouchInput();
            HandleSpacebarReset();
        }

        private void HandleKeyboardInput()
        {
            if (Input.GetKeyUp(KeyCode.A) && Input.GetKeyUp(KeyCode.D) && Input.GetKeyUp(KeyCode.S) && Input.GetKeyUp(KeyCode.W))
            {
                _movementIntervalTimer = 0f;
            }

            _movementIntervalTimer -= Time.deltaTime;

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                MoveCamera(CameraMoveDir.LEFT);
            }
            else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                MoveCamera(CameraMoveDir.RIGHT);
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                MoveCamera(CameraMoveDir.DOWN);
            }
            else if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                MoveCamera(CameraMoveDir.UP);
            }

            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                UpdateCameraZoom(CameraZoom.INZOOM);
            }
            else if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                UpdateCameraZoom(CameraZoom.OUTZOOM);
            }
        }

        private void HandleMouseInput()
        {
            // On touch devices Unity simulates mouse input from the first finger.
            // Skip the mouse pan/zoom path while touches are active so HandleTouchInput
            // is the single owner of touch panning (otherwise one finger pans twice).
            if (Input.touchCount > 0)
                return;

            // Mouse drag panning with left or middle mouse button
            // Latch: if left-click started over UI, suppress the entire drag until mouse-up
            if (Input.GetMouseButtonDown(0))
                _leftMouseStartedOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (Input.GetMouseButtonUp(0))
                _leftMouseStartedOverUI = false;

            bool leftMouseDown = !_leftMouseStartedOverUI && Input.GetMouseButtonDown(0);
            bool middleMouseDown = Input.GetMouseButtonDown(2);
            bool leftMouseHeld = !_leftMouseStartedOverUI && Input.GetMouseButton(0);
            bool middleMouseHeld = Input.GetMouseButton(2);
            bool leftMouseUp = Input.GetMouseButtonUp(0);
            bool middleMouseUp = Input.GetMouseButtonUp(2);

            if (leftMouseDown || middleMouseDown)
            {
                _lastMousePosition = Input.mousePosition;
            }

            if (leftMouseHeld || middleMouseHeld)
            {
                Vector3 currentMousePosition = Input.mousePosition;
                Vector3 mouseDelta = currentMousePosition - _lastMousePosition;

                // For left mouse: Only start dragging if we've moved enough (prevents accidental drags on clicks)
                // For middle mouse: Start dragging immediately
                if (!_isDragging && (middleMouseHeld || mouseDelta.magnitude > 2.0f))
                {
                    _isDragging = true;
                }

                if (_isDragging)
                {
                    // Convert screen space delta to world space
                    Vector3 worldDelta = _camera.ScreenToWorldPoint(new Vector3(mouseDelta.x, mouseDelta.y, _camera.nearClipPlane));
                    worldDelta -= _camera.ScreenToWorldPoint(Vector3.zero);

                    // Apply movement with sensitivity
                    UpdateCameraPositionSmooth(-worldDelta.x * _mouseSensitivity, -worldDelta.y * _mouseSensitivity);
                }

                _lastMousePosition = currentMousePosition;
            }
            else if (leftMouseUp || middleMouseUp)
            {
                _isDragging = false;
            }

            // Mouse scroll wheel zoom — each notch steps one discrete pixel-perfect level
            float scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                StepZoomAtPosition(scrollDelta > 0 ? -1 : 1, Input.mousePosition);
            }
        }

        private void HandleTouchInput()
        {
#if UNITY_ANDROID || UNITY_IOS || UNITY_EDITOR
            // Single finger drag panning
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Began)
                {
                    // Defer drag until the finger moves past the threshold so a
                    // stationary tap still reaches GridManager cell selection
                    // (UpdateInput runs before the Fire1 check in HandleInput).
                    _isDragging = false;
                    _touchStartPosition = touch.position;
                    _lastMousePosition = touch.position;
                }
                else if (touch.phase == TouchPhase.Moved)
                {
                    if (!_isDragging && (touch.position - _touchStartPosition).magnitude > TouchDragThreshold)
                    {
                        _isDragging = true;
                    }

                    if (_isDragging)
                    {
                        Vector3 currentTouchPosition = touch.position;
                        Vector3 touchDelta = currentTouchPosition - _lastMousePosition;

                        // Convert screen space delta to world space
                        Vector3 worldDelta = _camera.ScreenToWorldPoint(new Vector3(touchDelta.x, touchDelta.y, _camera.nearClipPlane));
                        worldDelta -= _camera.ScreenToWorldPoint(Vector3.zero);

                        // Apply movement with sensitivity
                        UpdateCameraPositionSmooth(-worldDelta.x * _mouseSensitivity, -worldDelta.y * _mouseSensitivity);
                    }

                    _lastMousePosition = touch.position;
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _isDragging = false;
                }
            }
            // Two finger pinch zoom
            else if (Input.touchCount == 2)
            {
                _isDragging = false;

                Touch touch1 = Input.GetTouch(0);
                Touch touch2 = Input.GetTouch(1);

                // Get current distance between fingers
                float currentDistance = Vector2.Distance(touch1.position, touch2.position);

                // Get previous distance between fingers
                Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;
                Vector2 touch2PrevPos = touch2.position - touch2.deltaPosition;
                float prevDistance = Vector2.Distance(touch1PrevPos, touch2PrevPos);

                // Accumulate pinch distance — step one zoom level per threshold
                const float pinchThreshold = 50f;
                _pinchZoomAccumulator += currentDistance - prevDistance;

                if (Mathf.Abs(_pinchZoomAccumulator) >= pinchThreshold)
                {
                    Vector3 midpoint = (touch1.position + touch2.position) / 2f;
                    StepZoomAtPosition(_pinchZoomAccumulator > 0 ? -1 : 1, midpoint);
                    _pinchZoomAccumulator = 0f;
                }
            }
            else
            {
                _isDragging = false;
                _pinchZoomAccumulator = 0f;
            }
#endif
        }

        private void HandleSpacebarReset()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ResetCameraToDefault();
            }
        }

        private void CalculateBounds()
        {
            float cellStep = _gridManager.Coords.CellStep;
            float halfCell = cellStep * 0.5f;
            int cols = (int)_gridManager.GridSize.x;
            int rows = (int)_gridManager.GridSize.y;

            // Visual tile extent: the dual-grid offset means rendered tiles
            // extend a full cell beyond the outermost cell centers.
            _gridLeft = -cellStep;
            _gridRight = cols * cellStep;
            _gridTop = cellStep;
            _gridBottom = -rows * cellStep;
        }

        private void CalculateMovementUnits()
        {
            float cellStep = _gridManager.Coords.CellStep;
            _movementUnits.x = cellStep;
            _movementUnits.y = cellStep;
        }

        public void SetBoundsClampingEnabled(bool enabled)
        {
            _boundsClampingEnabled = enabled;
        }

        public void SetZoomConfig(float contentBuffer, float maxZoomOutMultiplier)
        {
            _contentBuffer = contentBuffer;
            _maxZoomOutMultiplier = maxZoomOutMultiplier;
        }

        private Vector3 ClampPositionToBounds(Vector3 requestedPosition)
        {
            if (!_boundsClampingEnabled)
                return new Vector3(requestedPosition.x, requestedPosition.y, _zPlane);

            float halfViewH = _camera.orthographicSize;
            float halfViewW = halfViewH * _camera.aspect;

            float minX = _gridLeft + halfViewW;
            float maxX = _gridRight - halfViewW;
            float minY = _gridBottom + halfViewH;
            float maxY = _gridTop - halfViewH;

            // If the viewport is larger than the grid in a dimension, center on that axis
            float cx = minX <= maxX ? Mathf.Clamp(requestedPosition.x, minX, maxX) : (_gridLeft + _gridRight) * 0.5f;
            float cy = minY <= maxY ? Mathf.Clamp(requestedPosition.y, minY, maxY) : (_gridTop + _gridBottom) * 0.5f;

            return new Vector3(cx, cy, _zPlane);
        }

        private void UpdateCameraPosition(float moveX, float moveY)
        {
            Vector3 requestedPosition = new Vector3(this.transform.localPosition.x + moveX, this.transform.localPosition.y + moveY, _zPlane);
            this.transform.localPosition = ClampPositionToBounds(requestedPosition);
            _movementIntervalTimer = _movementIntervalDuration;
        }

        private void UpdateCameraPositionSmooth(float moveX, float moveY)
        {
            Vector3 requestedPosition = new Vector3(this.transform.localPosition.x + moveX, this.transform.localPosition.y + moveY, _zPlane);

            // Always clamp to bounds for smooth movement
            Vector3 clampedPosition = ClampPositionToBounds(requestedPosition);
            this.transform.localPosition = clampedPosition;
        }

        public void MoveCamera(CameraMoveDir dir)
        {
            if (_movementIntervalTimer > 0f)
            {
                return;
            }

            switch (dir)
            {
                case (CameraMoveDir.UP):
                    UpdateCameraPosition(0f, _movementUnits.y);
                    break;
                case (CameraMoveDir.DOWN):
                    UpdateCameraPosition(0f, -_movementUnits.y);
                    break;
                case (CameraMoveDir.LEFT):
                    UpdateCameraPosition(-_movementUnits.x, 0f);
                    break;
                case (CameraMoveDir.RIGHT):
                    UpdateCameraPosition(_movementUnits.x, 0f);
                    break;
                default:
                    break;
            }

        }

        public void FocusCamera(int x, int y)
        {
            this.transform.localPosition = new Vector3(x, -y, _zPlane);
        }

        public void CenterCamera()
        {
            float cx = (_gridLeft + _gridRight) * 0.5f;
            float cy = (_gridTop + _gridBottom) * 0.5f;
            this.transform.localPosition = ClampPositionToBounds(new Vector3(cx, cy, _zPlane));
        }

        public void UpdateCameraZoom(CameraZoom zoomDirection)
        {
            switch (zoomDirection)
            {
                case CameraZoom.INZOOM:
                    StepZoom(-1);
                    break;
                case CameraZoom.OUTZOOM:
                    StepZoom(1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(zoomDirection), zoomDirection, null);
            }
        }

        /// <summary>
        /// Step through discrete pixel-perfect zoom levels.
        /// direction &lt; 0 = zoom in (lower index), direction &gt; 0 = zoom out (higher index).
        /// </summary>
        private void StepZoom(int direction)
        {
            int newIndex = Mathf.Clamp(_currentZoomIndex + direction, 0, _zoomLevels.Length - 1);
            if (newIndex == _currentZoomIndex) return;

            _currentZoomIndex = newIndex;
            _currentZoomLevel = _zoomLevels[_currentZoomIndex];
            ApplyZoom();
        }

        /// <summary>
        /// Step one zoom level while keeping the world point under screenPosition stable.
        /// </summary>
        private void StepZoomAtPosition(int direction, Vector3 screenPosition)
        {
            int newIndex = Mathf.Clamp(_currentZoomIndex + direction, 0, _zoomLevels.Length - 1);
            if (newIndex == _currentZoomIndex) return;

            Vector3 worldPosBefore = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, _camera.nearClipPlane));

            _currentZoomIndex = newIndex;
            _currentZoomLevel = _zoomLevels[_currentZoomIndex];
            ApplyZoom();

            Vector3 worldPosAfter = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, _camera.nearClipPlane));
            Vector3 diff = worldPosBefore - worldPosAfter;
            UpdateCameraPositionSmooth(diff.x, diff.y);
        }

        public void ResetCameraToDefault()
        {
            CenterCamera();
            // Snap back to the level closest to 1.0
            _currentZoomIndex = FindClosestZoomIndex(1.0f);
            _currentZoomLevel = _zoomLevels[_currentZoomIndex];
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            _camera.orthographicSize = _baseOrthoSize * _currentZoomLevel;
        }

        /// <summary>
        /// Recompute zoom levels after entity data is available.
        /// contentTiles = max dimension of populated cells (in tile count).
        /// Default zoom fits contentTiles + buffer; max zoom-out is a multiplier of that.
        /// </summary>
        public void FitToContent(float contentTiles)
        {
            float defaultTiles = contentTiles + _contentBuffer;
            float defaultZoom = defaultTiles / (_baseOrthoSize * 2f);

            ComputeZoomLevels(defaultZoom);

            CalculateBounds();
            CenterCamera();
            ApplyZoom();
        }

        /// <summary>
        /// Build logarithmically-spaced zoom levels.
        /// defaultZoom = the zoom multiplier where the default view fits the content.
        /// </summary>
        private void ComputeZoomLevels(float defaultZoom = 1.0f)
        {
            float baseTilesVisible = _baseOrthoSize * 2f;
            float minZoom = Mathf.Max(0.01f, _minTilesVisible / baseTilesVisible);
            float maxZoom = Mathf.Max(minZoom + 0.01f, defaultZoom * _maxZoomOutMultiplier);

            int steps = Mathf.Max(2, _zoomSteps);
            _zoomLevels = new float[steps + 1];

            float logMin = Mathf.Log(minZoom);
            float logMax = Mathf.Log(maxZoom);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                _zoomLevels[i] = Mathf.Exp(Mathf.Lerp(logMin, logMax, t));
            }

            _currentZoomIndex = FindClosestZoomIndex(defaultZoom);
            _currentZoomLevel = _zoomLevels[_currentZoomIndex];
        }

        private int FindClosestZoomIndex(float target)
        {
            int best = 0;
            float bestDist = Mathf.Abs(_zoomLevels[0] - target);
            for (int i = 1; i < _zoomLevels.Length; i++)
            {
                float dist = Mathf.Abs(_zoomLevels[i] - target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        public float GetCurrentZoomPercentage()
        {
            return (1.0f / _currentZoomLevel) * 100f;
        }

        public bool IsDragging()
        {
            return _isDragging;
        }
    }
}
