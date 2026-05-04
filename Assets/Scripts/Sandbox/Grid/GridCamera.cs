using System;
using System.Collections.Generic;
using UnityEngine;

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
        private Bounds _cameraBounds;
        private Vector2 _movementUnits;

        [SerializeField] float _movementIntervalDuration;
        private float _movementIntervalTimer;

        [Header("Mouse/Touch Controls")]
        private bool _isDragging = false;
        private Vector3 _lastMousePosition;
        private float _baseOrthographicSize;
        private float _currentZoomLevel = 1.0f;
        private const float _minZoomLevel = 0.5f; // 200% zoom in
        private const float _maxZoomLevel = 5.0f; // 20% zoom in (5x zoom out, fits the 80x40 Largo map with margin)
        [SerializeField] private float _mouseSensitivity = 1.0f;
        [SerializeField] private float _scrollSensitivity = 0.1f;

        private const string LogChannel = "[GridCamera]";

        [SerializeField] private List<float> zoomValues = new();
        private int _currentZoomIndex;

        public void Init(GridManager gridManager)
        {
            _gridManager = gridManager;

            CalculateBounds();
            CalculateMovementUnits();
            CenterCamera();
            _currentZoomIndex = 2;
            _camera.orthographicSize = zoomValues[_currentZoomIndex];
            
            // Initialize mouse/touch controls
            _baseOrthographicSize = zoomValues[_currentZoomIndex];
            _currentZoomLevel = 1.0f;
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
            // Mouse drag panning with left or middle mouse button
            bool leftMouseDown = Input.GetMouseButtonDown(0);
            bool middleMouseDown = Input.GetMouseButtonDown(2);
            bool leftMouseHeld = Input.GetMouseButton(0);
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

            // Mouse scroll wheel zoom
            float scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                UpdateScrollZoom(scrollDelta);
            }
        }

        private void HandleTouchInput()
        {
#if UNITY_MOBILE || UNITY_EDITOR
            // Single finger drag panning
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                
                if (touch.phase == TouchPhase.Began)
                {
                    _isDragging = true;
                    _lastMousePosition = touch.position;
                }
                else if (touch.phase == TouchPhase.Moved && _isDragging)
                {
                    Vector3 currentTouchPosition = touch.position;
                    Vector3 touchDelta = currentTouchPosition - _lastMousePosition;
                    
                    // Convert screen space delta to world space
                    Vector3 worldDelta = _camera.ScreenToWorldPoint(new Vector3(touchDelta.x, touchDelta.y, _camera.nearClipPlane));
                    worldDelta -= _camera.ScreenToWorldPoint(Vector3.zero);
                    
                    // Apply movement with sensitivity
                    UpdateCameraPositionSmooth(-worldDelta.x * _mouseSensitivity, -worldDelta.y * _mouseSensitivity);
                    
                    _lastMousePosition = currentTouchPosition;
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
                
                // Calculate zoom delta
                float deltaDistance = currentDistance - prevDistance;
                if (Mathf.Abs(deltaDistance) > 1.0f)
                {
                    float zoomDelta = deltaDistance * 0.01f;
                    
                    // Calculate midpoint between the two touches
                    Vector3 midpoint = (touch1.position + touch2.position) / 2f;
                    
                    // Zoom at the midpoint between touches
                    UpdateScrollZoomAtPosition(zoomDelta, midpoint);
                }
            }
            else
            {
                _isDragging = false;
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
            float totalWidth = ((_gridManager.CellScale.x * _gridManager.CellSize.x) + _gridManager.CellGap) * _gridManager.GridSize.x;
            float totalHeight = ((_gridManager.CellScale.y * _gridManager.CellSize.y) + _gridManager.CellGap) * _gridManager.GridSize.y;

            // Calculate viewport size at maximum zoom out (200% = 2x orthographic size)
            float maxZoomOutHeight = _baseOrthographicSize * _maxZoomLevel * 2f; // _maxZoomLevel is 2.0
            float maxZoomOutWidth = maxZoomOutHeight * _camera.aspect;
            
            // Calculate viewport size at maximum zoom in (50% = 0.5x orthographic size)  
            float maxZoomInHeight = _baseOrthographicSize * _minZoomLevel * 2f; // _minZoomLevel is 0.5
            float maxZoomInWidth = maxZoomInHeight * _camera.aspect;
            
            // Bounds should allow camera to show all grid content at any zoom level
            // When zoomed out, camera needs room to move to show edges
            // When zoomed in, camera needs even more room to pan across the whole grid
            
            float minX = -maxZoomOutWidth / 2f;
            float maxX = totalWidth + maxZoomOutWidth / 2f;
            float minY = -totalHeight - maxZoomOutHeight / 2f;
            float maxY = maxZoomOutHeight / 2f;
            
            Vector3 center = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, _zPlane);
            Vector3 size = new Vector3(maxX - minX, maxY - minY, 0f);
            
            _cameraBounds = new Bounds(center, size);
        }

        private void CalculateMovementUnits()
        {
            _movementUnits.x = (_gridManager.CellScale.x * _gridManager.CellSize.x) + _gridManager.CellGap;
            _movementUnits.y = (_gridManager.CellScale.y * _gridManager.CellSize.y) + _gridManager.CellGap;
        }

        private Vector3 ClampPositionToBounds(Vector3 requestedPosition)
        {
            return new Vector3(
                Mathf.Clamp(requestedPosition.x, _cameraBounds.min.x, _cameraBounds.max.x),
                Mathf.Clamp(requestedPosition.y, _cameraBounds.min.y, _cameraBounds.max.y),
                _zPlane
            );
        }

        private void UpdateCameraPosition(float moveX, float moveY)
        {
            Vector3 requestedPosition = new Vector3(this.transform.localPosition.x + moveX, this.transform.localPosition.y + moveY, _zPlane);

            if (_cameraBounds.Contains(requestedPosition))
            {
                this.transform.localPosition = requestedPosition;
                _movementIntervalTimer = _movementIntervalDuration;
            }
            else
            {
                // Clamp to bounds
                Vector3 clampedPosition = ClampPositionToBounds(requestedPosition);
                this.transform.localPosition = clampedPosition;
            }
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
            this.transform.localPosition = _cameraBounds.center;
        }

        public void UpdateCameraZoom(CameraZoom zoomDirection)
        {
            switch (zoomDirection)
            {
                case CameraZoom.INZOOM:
                    {
                        if (_currentZoomIndex < zoomValues.Count - 1)
                        {
                            _currentZoomIndex++;
                        }
                        break;
                    }
                case CameraZoom.OUTZOOM:
                    {
                        if (_currentZoomIndex > 0)
                        {
                            _currentZoomIndex--;
                        }
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(zoomDirection), zoomDirection, null);
            }

            _camera.orthographicSize = zoomValues[_currentZoomIndex];
            // Sync the continuous zoom level with discrete zoom
            _currentZoomLevel = _camera.orthographicSize / _baseOrthographicSize;
        }

        private void UpdateScrollZoom(float scrollDelta)
        {
            UpdateScrollZoomAtPosition(scrollDelta, Input.mousePosition);
        }
        
        private void UpdateScrollZoomAtPosition(float scrollDelta, Vector3 screenPosition)
        {
            // Get world position before zoom
            Vector3 worldPositionBeforeZoom = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, _camera.nearClipPlane));
            
            // Update zoom level
            float zoomChange = scrollDelta * _scrollSensitivity;
            float oldZoomLevel = _currentZoomLevel;
            _currentZoomLevel = Mathf.Clamp(_currentZoomLevel - zoomChange, _minZoomLevel, _maxZoomLevel);
            
            // Apply zoom
            _camera.orthographicSize = _baseOrthographicSize * _currentZoomLevel;
            
            // Get world position after zoom
            Vector3 worldPositionAfterZoom = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, _camera.nearClipPlane));
            
            // Calculate the difference and adjust camera position
            Vector3 worldPositionDifference = worldPositionBeforeZoom - worldPositionAfterZoom;
            Vector3 newCameraPosition = transform.localPosition + worldPositionDifference;
            
            // Apply position with bounds checking
            UpdateCameraPositionSmooth(worldPositionDifference.x, worldPositionDifference.y);
        }

        public void ResetCameraToDefault()
        {
            // Reset position to center
            CenterCamera();
            
            // Reset zoom to 100% (base level)
            _currentZoomLevel = 1.0f;
            _camera.orthographicSize = _baseOrthographicSize;
            
            // Also reset discrete zoom index
            _currentZoomIndex = 2;
        }
        
        // Public method to get current zoom percentage for UI display
        public float GetCurrentZoomPercentage()
        {
            return (1.0f / _currentZoomLevel) * 100f;
        }
        
        // Public method to check if camera is currently being dragged
        public bool IsDragging()
        {
            return _isDragging;
        }
    }
}
