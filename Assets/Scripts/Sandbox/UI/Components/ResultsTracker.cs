using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Glitchers.EcoKnow.Sandbox.UI
{
    public class ResultsTracker : MonoBehaviour
    {
        [Header("Widgets")]
        [SerializeField] private ResultsWidget _resultsWidgetPrefab;
        [SerializeField] private Transform _resultsContainer;

        [Header("Settings")]
        [SerializeField] private bool _displayAllResults = false;
        [SerializeField] private int _maxResultsWidgets = 10;
        [SerializeField] private int _maxFutureRoundCount = 2;

        [SerializeField] private bool _displayActive = true;

        // Cached widget array; refreshed in Init() after the widget instances are spawned.
        // The previous property hit GetComponentsInChildren on every access and was called
        // multiple times per UpdateResultsTrack pass.
        private ResultsWidget[] _cachedWidgets = Array.Empty<ResultsWidget>();
        private ResultsWidget[] _resultWidgets => _cachedWidgets;
        public ResultsWidget ActiveWidget => _cachedWidgets == null ? null : _cachedWidgets.FirstOrDefault(x => x.IsActive);

        private const string LogChannel = "[ResultsTracker]";

        public void Init(List<WinCondition.Result> results, int currentRound, int maxRounds)
        {
            //Set up correct number of widgets
            if ((_resultsWidgetPrefab == null) || (_resultsContainer == null))
            {
                Debug.LogError($"{LogChannel} Failed setup, Widget prefab or Container is null!");
                return;
            }

            //Remove old objectives
            foreach (Transform child in _resultsContainer)
            {
                Destroy(child.gameObject);
            }

            //Check how many widgets to display
            if (_displayAllResults || (_maxResultsWidgets <= 0) || (_maxResultsWidgets > SandboxManager.Instance.MaxRounds))
            {
                _maxResultsWidgets = SandboxManager.Instance.MaxRounds;
            }

            //Instantiate new ones. Set initial state inline (not just in the post-frame
            //coroutine) so ActiveWidget is non-null by the time the first OnNewRoundStarted
            //fires — otherwise UpdateActiveWidgetTier / UpdateActiveWidget run on round 0
            //before the coroutine wakes and silently no-op, leaving the widget showing its
            //prefab default text ("999999" / "New Text").
            //
            //Track instances explicitly into the cache list rather than calling
            //GetComponentsInChildren at the end — the foreach above only marks the old
            //widgets for destruction (Unity defers Destroy to end-of-frame), so a hierarchy
            //walk now would return both the new widgets AND the soon-dead old ones,
            //leaving MissingReference refs in the cache.
            List<ResultsWidget> created = new List<ResultsWidget>(_maxResultsWidgets);
            for (int i = 0; i < _maxResultsWidgets; i++)
            {
                ResultsWidget widget = Instantiate(_resultsWidgetPrefab, _resultsContainer);
                widget.SetState(i == 0 ? (int)ResultsWidget.State.ACTIVE : (int)ResultsWidget.State.FUTURE);
                created.Add(widget);
            }
            _cachedWidgets = created.ToArray();

            StartCoroutine(SetupTrack(results, currentRound, maxRounds));
        }

        private IEnumerator SetupTrack(List<WinCondition.Result> results, int currentRound, int maxRounds)
        {
            yield return new WaitForEndOfFrame();

            ResetResultsTrack();
            UpdateResultsTrack(results, currentRound, maxRounds);
        }

        public void ResetResultsTrack()
        {
            for (int i = 0; i < _resultWidgets.Count(); i++)
            {
                ResultsWidget widget = _resultWidgets[i];
                widget.SetState(i == 0 ? (int)ResultsWidget.State.ACTIVE : (int)ResultsWidget.State.FUTURE);
            }
        }

        public void UpdateResultsTrack(List<WinCondition.Result> results, int currentRound, int maxRounds)
        {
            if ((maxRounds <= 0) || (currentRound < 0) || (currentRound > maxRounds))
            {
                Debug.LogWarning($"{LogChannel} Unable to update Results Tracker, currentRound [{currentRound}] and/or maxRounds [{maxRounds}] are invalid");
                return;
            }

            if (_resultWidgets != null)
            {
                bool failed = results.Contains(WinCondition.Result.FAILED);
                bool hasAllResults = (results.Count() >= maxRounds);

                int roundsRemaining = maxRounds - currentRound;
                int futureRoundCount = hasAllResults ? 0 : Mathf.Min(roundsRemaining, _maxFutureRoundCount);
                if (!failed && _displayActive && !hasAllResults)
                {
                    futureRoundCount += 1; //+1 for the Active Round
                }

                int displayedResultCount = Mathf.Clamp(_resultWidgets.Count() - futureRoundCount, 0, results.Count);
                //Debug.Log("Displayed results: " + displayedResultCount);

                List<WinCondition.Result> trimmedResults = results.GetRange(results.Count - displayedResultCount, displayedResultCount);

                for (int i = 0; i < _resultWidgets.Count(); i++)
                {
                    ResultsWidget widget = _resultWidgets[i];
                    if (i < displayedResultCount)
                    {
                        widget?.SetState((int)trimmedResults[i]);
                    }
                    else if (i == displayedResultCount)
                    {
                        //Don't show active state if win condition has been failed
                        if (!results.Contains(WinCondition.Result.FAILED))
                        {
                            widget?.SetState((int)ResultsWidget.State.ACTIVE);
                        }
                        else
                        {
                            widget?.SetState((int)ResultsWidget.State.FUTURE);
                        }
                    }
                    else
                    {
                        widget?.SetState((int)ResultsWidget.State.FUTURE);
                    }
                }
            }
        }


        public void UpdateActiveWidget(int population, float lowerLimit, float upperLimit)
        {
            if (ActiveWidget != null)
            {
                ActiveWidget.SetActiveQuantity(population);
                ActiveWidget.SetRange(population, lowerLimit, upperLimit);
            }

            RefreshTracker();
        }

        // Tier display path used by win conditions that target a pollutant entity
        // (DisplayAsPollutionTier=true). Shows "LOW" / "MED" / "HIGH" coloured instead of
        // a numeric range so the player isn't reading raw 10^14 values.
        public void UpdateActiveWidgetTier(string tier, Color tierColor)
        {
            if (ActiveWidget != null)
            {
                ActiveWidget.SetActiveTier(tier, tierColor);
            }
            RefreshTracker();
        }

        public void RefreshTracker()
        {
            StartCoroutine(RefreshLayout());
        }

        private IEnumerator RefreshLayout()
        {
            yield return new WaitForEndOfFrame();

            //Now force layout update
            for (int i = 0; i < _resultWidgets.Count(); i++)
            {
                ResultsWidget widget = _resultWidgets[i];
                RectTransform widgetRect = widget.GetComponent<RectTransform>();
                if (widgetRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(widgetRect);
                }
            }

            if (_resultsContainer != null)
            {
                RectTransform resultsRect = _resultsContainer.GetComponent<RectTransform>();
                if (resultsRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(resultsRect);
                }
            }
        }
    }
}
