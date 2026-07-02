using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections;
using UnityEngine;

namespace SamSWAT.FireSupport.ArysReloaded.Unity;

public sealed class UavDeviceController : Player.UsableItemController, IOnHandsUseCallback
{
	private const int HandsLayer = 1;
	private const float TapAudioDelaySeconds = 7f / 30f;
	private const float StartupAnimatorLogSeconds = 2f;
	private const float StartupAnimatorLogIntervalSeconds = 0.25f;
	private const float AuthorizationIdleWaitSeconds = 6f;
	private const float PortraitOutroPoseTimeoutSeconds = 1.45f;
	private const float ConfirmAnimatorWaitTimeoutSeconds = 1.25f;
	private const float AuthorizingFadeSeconds = 0.35f;
	private const float AuthorizedFadeSeconds = 0.45f;
	private const string TapAudioClipName = "Blastgang_finger_tap_oneshot_FP";

	private Player _ownerPlayer;
	private AudioSource _tapAudioSource;
	private UavPhoneScreenRenderer _phoneScreen;
	private TerraGroupPhoneState _phoneState = TerraGroupPhoneState.Home;
	private ESupportType _selectedSupportType = ESupportType.Uav;
	private bool _authorizationSessionActive;
	private bool _authorizationInputLocked;
	private Callback<IOnHandsUseCallback> _onUsedCallback;
	private event Action<UavDeviceController, bool> _authorizationSessionFinished;
	private bool _finishNotified;
	private bool _finishPending;
	private bool _finishSuccess;
	private bool _authorizationOutroPreplayed;
	private bool _phoneAnimatorSpeedCustomized;
	private bool _confirmationSequenceRunning;
	private bool _paymentAttempted;
	private bool _authorizationGranted;
	private bool _restoreStarted;
	private bool _phoneVisualTerminalPhaseSent;
	private Coroutine _confirmationSequenceCoroutine;

	public Animator PhoneAnimator { get; private set; }
	public UavPhoneLaunchMode LaunchMode { get; set; } = UavPhoneLaunchMode.ManualAuthorization;
	public bool IsAuthorizationSessionActive => _authorizationSessionActive;

	public static bool ShouldSuppressQuickUse(Player player)
	{
		return player?.HandsController is UavDeviceController controller &&
		       controller.LaunchMode == UavPhoneLaunchMode.ManualAuthorization;
	}

	public event Action<UavDeviceController, bool> AuthorizationSessionFinished
	{
		add
		{
			_authorizationSessionFinished += value;
			if (_finishPending)
			{
				value?.Invoke(this, _finishSuccess);
			}
		}
		remove => _authorizationSessionFinished -= value;
	}

	/// <summary>
	/// True when EFT's quick-use flow owns this session: EFT installed a
	/// completion callback and will restore the previous item itself after
	/// the session finishes. External code must not run its own restore
	/// (DestroyController/TrySetLastEquippedWeapon) on top of that, or the
	/// two hand swaps race and wedge EFT's interaction state machine.
	/// </summary>
	public bool IsQuickUseSession => _onUsedCallback != null;

	public void SetOnUsedCallback(Callback<IOnHandsUseCallback> callback)
	{
		_onUsedCallback = callback;
	}

	public Callback<IOnHandsUseCallback> GetOnUsedCallback()
	{
		return _onUsedCallback;
	}

	private void Awake()
	{
		TscDiagnostics.LogPhone($"TSC Uplink controller awake on '{gameObject.name}'.");
	}

	public override void vmethod_0(Player player, WeaponPrefab weaponPrefab)
	{
		TscDiagnostics.LogPhone(
			$"TSC Uplink controller binding prefab '{(weaponPrefab == null ? "<null>" : weaponPrefab.gameObject.name)}'. {DescribeItem(Item)}");

		try
		{
			_ownerPlayer = player;
			base.vmethod_0(player, weaponPrefab);

			if (weaponPrefab == null)
			{
				FireSupportPlugin.LogSource.LogWarning(
					$"UAV activation device controller loaded without a usable prefab. {DescribeItem(Item)}");
				return;
			}

			PhoneAnimator = weaponPrefab.GetComponentInChildren<Animator>(true);
			if (PhoneAnimator == null)
			{
				FireSupportPlugin.LogSource.LogWarning("UAV activation device prefab has no phone animator.");
			}
			else
			{
				int layer = GetHandsLayer(PhoneAnimator);
				PhoneAnimator.SetBool("Tap", false);
				PhoneAnimator.SetBool("Success", false);
				PhoneAnimator.SetBool("Fail", false);
				PhoneAnimator.Play("Spawn", layer, 0f);
				PhoneAnimator.Update(0f);
				PhoneAnimator.SetBool("Active", true);

				TscDiagnostics.LogPhone(
					$"TSC Uplink animator bound. runtimeItemType={Item?.GetType().FullName ?? "<null>"}, parentTpl={Item?.Template?.ParentId?.ToString() ?? "<null>"}, usablePrefab={(weaponPrefab == null ? "<null>" : weaponPrefab.gameObject.name)}, animatorController={PhoneAnimator.runtimeAnimatorController?.name ?? "<null>"}.");
				if (TscDiagnostics.VerbosePhone)
				{
					StartCoroutine(LogAnimatorStartup(weaponPrefab));
				}
			}

			BindTapAudio(weaponPrefab);

			if (LaunchMode == UavPhoneLaunchMode.InternalUavActivation)
			{
				TscDiagnostics.LogPhone("TSC Uplink authorization phone session skipped for internal activation launch mode.");
				return;
			}

			if (FireSupportPayment.GetActivePaymentMode() != PaymentMode.DirectRadial)
			{
				if (PhoneAnimator == null)
				{
					FireSupportPlugin.LogSource.LogWarning("TerraGroup phone authorization deferred: phone animator was null.");
					if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
					{
						NotifyAuthorizationFinished(success: false);
					}
				}
				else
				{
					StartPhoneScreen(weaponPrefab, TerraGroupPhoneState.Home, "ManualAuthorizationBoot");
					StartCoroutine(StartAuthorizationSessionWhenIdle(weaponPrefab));
				}
			}
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device controller failed to initialize. {ex}");
			if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
			{
				NotifyAuthorizationFinished(success: false);
			}
		}
	}

	public void PlayTap(float crossFadeSeconds = 0f)
	{
		if (PhoneAnimator == null)
		{
			return;
		}

		try
		{
			ResetPhoneAnimatorSpeed("tap");
			if (crossFadeSeconds > 0f)
			{
				PhoneAnimator.CrossFade("Tap", crossFadeSeconds, GetHandsLayer(PhoneAnimator), 0f);
			}
			else
			{
				PhoneAnimator.Play("Tap", GetHandsLayer(PhoneAnimator), 0f);
			}

			if (_tapAudioSource?.clip != null)
			{
				StartCoroutine(PlayTapAudioAfter(TapAudioDelaySeconds));
			}
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device tap animation failed. {ex}");
		}
	}

	public void PlayOutroSuccess()
	{
		PlayOutro("Outro Success");
	}

	public void PlayOutroFail()
	{
		PlayOutro("Outro Fail");
	}

	public void CancelAuthorizationSession()
	{
		FireSupportPlugin.LogSource.LogInfo("TSC phone session cancelled.");
		if (_confirmationSequenceRunning && (_paymentAttempted || _restoreStarted))
		{
			TscDiagnostics.LogPhone("TSC phone cancel ignored: confirmation payment/restore is already committed.");
			return;
		}

		if (_authorizationSessionActive || LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
		{
			if (_confirmationSequenceRunning)
			{
				CancelConfirmationSequenceBeforeCommit();
				return;
			}

			FinishAuthorizationSession(playOutro: true, success: false);
		}
	}

	private void Update()
	{
		if (!_authorizationSessionActive)
		{
			return;
		}

		// While the inventory screen is open, mouse clicks and hotkeys belong to
		// the inventory UI, not the phone.
		if (_ownerPlayer != null && _ownerPlayer.IsInventoryOpened)
		{
			return;
		}

		if (_confirmationSequenceRunning)
		{
			if (!_paymentAttempted &&
			    !_restoreStarted &&
			    (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)))
			{
				CancelConfirmationSequenceBeforeCommit();
			}

			return;
		}

		if (_authorizationInputLocked)
		{
			return;
		}

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			FinishAuthorizationSession(playOutro: true, success: false);
			return;
		}

		// RMB steps back one screen instead of cancelling the whole session;
		// Escape remains the full cancel.
		if (Input.GetMouseButtonDown(1))
		{
			HandleBack();
			return;
		}

		if (IsRotatePromptState(_phoneState) && IsRotateConfirmPressed())
		{
			BeginConfirmationSequence();
			return;
		}

		if (_phoneState == TerraGroupPhoneState.ConfirmPaymentPortrait)
		{
			HandleConfirmInput();
			return;
		}

		if (HandleServiceSelectionShortcuts())
		{
			return;
		}

		if (Input.GetMouseButtonDown(0))
		{
			HandleTap();
		}
	}

	private void PlayOutro(string stateName)
	{
		if (PhoneAnimator == null)
		{
			return;
		}

		try
		{
			ResetPhoneAnimatorSpeed($"play outro {stateName}");
			PhoneAnimator.SetBool("Active", false);
			PhoneAnimator.Play(stateName, GetHandsLayer(PhoneAnimator), 0f);
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device outro animation failed. {ex}");
		}
	}

	private void BindTapAudio(WeaponPrefab weaponPrefab)
	{
		try
		{
			AudioSource prefabSource = weaponPrefab.GetComponentInChildren<AudioSource>(true);
			if (prefabSource?.clip != null)
			{
				prefabSource.playOnAwake = false;
				_tapAudioSource = prefabSource;
				return;
			}

			foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
			{
				if (clip == null ||
				    !string.Equals(clip.name, TapAudioClipName, System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				_tapAudioSource = gameObject.AddComponent<AudioSource>();
				_tapAudioSource.clip = clip;
				_tapAudioSource.playOnAwake = false;
				_tapAudioSource.spatialBlend = 0f;
				_tapAudioSource.volume = 0.8f;
				return;
			}
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device tap audio setup failed. {ex}");
		}
	}

	private IEnumerator StartAuthorizationSessionWhenIdle(WeaponPrefab weaponPrefab)
	{
		float stop = Time.unscaledTime + AuthorizationIdleWaitSeconds;
		while (PhoneAnimator != null && Time.unscaledTime < stop)
		{
			if (IsIdleLoop(PhoneAnimator))
			{
				TscDiagnostics.LogPhone("TSC phone animator reached Idle_Loop.");
				if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
				{
					TscDiagnostics.LogPhone("TSC manual uplink: Idle_Loop reached");
				}

				if (StartAuthorizationSession(weaponPrefab))
				{
					PlayTap(0.05f);
					ShowPhoneState(TerraGroupPhoneState.TacticalServices);
				}
				else if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
				{
					NotifyAuthorizationFinished(success: false);
				}

				yield break;
			}

			yield return null;
		}

		FireSupportPlugin.LogSource.LogWarning(
			$"TerraGroup phone authorization not started: animator did not reach Hands.Idle_Loop within {AuthorizationIdleWaitSeconds:0.0}s. {DescribeAnimatorState(PhoneAnimator, FindScreenRenderer(weaponPrefab?.transform))}");
		if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
		{
			NotifyAuthorizationFinished(success: false);
		}
	}

	private bool StartAuthorizationSession(WeaponPrefab weaponPrefab)
	{
		if (!StartPhoneScreen(weaponPrefab, TerraGroupPhoneState.Home, "ManualAuthorization"))
		{
			return false;
		}

		_authorizationSessionActive = true;
		_authorizationInputLocked = false;
		_confirmationSequenceRunning = false;
		_paymentAttempted = false;
		_authorizationGranted = false;
		_restoreStarted = false;
		_phoneVisualTerminalPhaseSent = false;
		_confirmationSequenceCoroutine = null;
		FireSupportPlugin.LogSource.LogInfo("TSC phone session started.");
		if (LaunchMode == UavPhoneLaunchMode.ManualAuthorization)
		{
			TscDiagnostics.LogPhone("TSC manual uplink authorization session started");
		}

		PublishPhoneVisualPhase(UavPhoneVisualPhase.StartPurchasePhone, duration: 9.0f);
		return true;
	}

	private bool StartPhoneScreen(WeaponPrefab weaponPrefab, TerraGroupPhoneState initialState, string context)
	{
		if (weaponPrefab == null)
		{
			return false;
		}

		if (_phoneScreen != null)
		{
			ShowPhoneState(initialState);
			return true;
		}

		try
		{
			Renderer screenRenderer = UavPhoneScreenRenderer.FindBestScreenRenderer(
				weaponPrefab.transform,
				context,
				logCandidates: true);
			if (screenRenderer == null)
			{
				FireSupportPlugin.LogSource.LogWarning("TerraGroup phone authorization UI skipped: screen mesh was not found.");
				return false;
			}

			screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			screenRenderer.receiveShadows = false;
			screenRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			screenRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

			_phoneScreen = gameObject.AddComponent<UavPhoneScreenRenderer>();
			_phoneScreen.Initialize(
				screenRenderer,
				UavPhoneScreenRenderer.CaptureScreenUVRect(screenRenderer),
				canvasRotation: 90f,
				BuildPhoneContext(),
				weaponPrefab.transform);
			ShowPhoneState(initialState);

			return true;
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone authorization UI failed to start. {ex}");
			return false;
		}
	}

	private bool HandleServiceSelectionShortcuts()
	{
		if (!CanSelectSupportType(_phoneState))
		{
			return false;
		}

		bool onePressed = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
		bool twoPressed = Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
		bool threePressed = Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);

		if (_phoneState == TerraGroupPhoneState.ServiceCategory)
		{
			if (onePressed)
			{
				Input.ResetInputAxes();
				PlayTap(0.05f);
				ShowPhoneState(TerraGroupPhoneState.RotateToConfirm);
				return true;
			}

			if (twoPressed)
			{
				ESupportType variantSupportType = _selectedSupportType switch
				{
					ESupportType.Extract => ESupportType.PriorityExfil,
					ESupportType.Strafe => ESupportType.DoubleStrafe,
					ESupportType.Uav => ESupportType.FocusedSweep,
					_ => ESupportType.None
				};

				if (variantSupportType == ESupportType.None)
				{
					Input.ResetInputAxes();
					TscDiagnostics.LogPhone("TSC phone ignored locked/offline service card shortcut.");
					return true;
				}

				Input.ResetInputAxes();
				if (!FireSupportServiceAvailability.IsServiceEnabled(variantSupportType))
				{
					TscDiagnostics.LogPhone($"TSC phone blocked disabled service card shortcut: {variantSupportType}.");
					FireSupportPayment.NotifyServiceUnavailable(variantSupportType);
					return true;
				}

				PlayTap(0.05f);
				SelectSupportType(variantSupportType);
				ShowPhoneState(TerraGroupPhoneState.RotateToConfirm);
				return true;
			}

			if (twoPressed || threePressed)
			{
				Input.ResetInputAxes();
				TscDiagnostics.LogPhone("TSC phone ignored locked/offline service card shortcut.");
				return true;
			}

			return false;
		}

		if (onePressed)
		{
			Input.ResetInputAxes();
			OpenServiceCategory(ESupportType.Extract);
			return true;
		}
		else if (twoPressed)
		{
			Input.ResetInputAxes();
			OpenServiceCategory(ESupportType.Strafe);
			return true;
		}
		else if (threePressed)
		{
			Input.ResetInputAxes();
			OpenServiceCategory(ESupportType.Uav);
			return true;
		}

		return false;
	}

	// One keypress opens the category directly. Requiring a separate LMB tap
	// after highlighting a tab was the single most reported point of confusion.
	private void OpenServiceCategory(ESupportType supportType)
	{
		SelectSupportType(supportType);
		PlayTap(0.05f);
		ShowPhoneState(TerraGroupPhoneState.ServiceCategory);
	}

	private void HandleTap()
	{
		PlayTap(0.05f);

		switch (_phoneState)
		{
			case TerraGroupPhoneState.Home:
				ShowPhoneState(TerraGroupPhoneState.TacticalServices);
				break;
			case TerraGroupPhoneState.TacticalServices:
				ShowPhoneState(TerraGroupPhoneState.ServiceCategory);
				break;
			case TerraGroupPhoneState.ServiceCategory:
				if (CanProceedWithSelectedSupport())
				{
					ShowPhoneState(TerraGroupPhoneState.RotateToConfirm);
				}
				break;
			case TerraGroupPhoneState.ServiceReview:
				if (CanProceedWithSelectedSupport())
				{
					ShowPhoneState(TerraGroupPhoneState.RotateToConfirm);
				}
				break;
			case TerraGroupPhoneState.RotateToConfirm:
				// Rotation/confirm is intentionally keyboard-gated so stray taps do not spend money.
				break;
			case TerraGroupPhoneState.Authorized:
			case TerraGroupPhoneState.Denied:
				FinishAuthorizationSession(playOutro: true, success: _phoneState == TerraGroupPhoneState.Authorized);
				break;
		}
	}

	private void HandleConfirmInput()
	{
		// Manual portrait swipe input is intentionally disabled for release.
		// The swipe is a visual overlay in the Enter-confirm sequence.
	}

	private void HandleBack()
	{
		PlayTap(0.05f);

		switch (_phoneState)
		{
			case TerraGroupPhoneState.TacticalServices:
				ShowPhoneState(TerraGroupPhoneState.Home);
				break;
			case TerraGroupPhoneState.ServiceCategory:
				ShowPhoneState(TerraGroupPhoneState.TacticalServices);
				break;
			case TerraGroupPhoneState.ServiceReview:
			case TerraGroupPhoneState.RotateToConfirm:
				ShowPhoneState(TerraGroupPhoneState.ServiceCategory);
				break;
			case TerraGroupPhoneState.Authorized:
			case TerraGroupPhoneState.Denied:
				FinishAuthorizationSession(playOutro: true, success: _phoneState == TerraGroupPhoneState.Authorized);
				break;
			default:
				// Home and any unexpected state: backing out closes the phone.
				FinishAuthorizationSession(playOutro: true, success: false);
				break;
		}
	}

	private static bool IsRotatePromptState(TerraGroupPhoneState state)
	{
		return state == TerraGroupPhoneState.RotateToConfirm ||
		       state == TerraGroupPhoneState.ServiceReview;
	}

	private static bool IsRotateConfirmPressed()
	{
		return Input.GetKeyDown(KeyCode.Return) ||
		       Input.GetKeyDown(KeyCode.KeypadEnter);
	}

	private static bool CanSelectSupportType(TerraGroupPhoneState state)
	{
		return state == TerraGroupPhoneState.TacticalServices ||
		       state == TerraGroupPhoneState.ServiceCategory;
	}

	private void BeginConfirmationSequence()
	{
		if (_confirmationSequenceRunning || _restoreStarted)
		{
			TscDiagnostics.LogPhone("TSC phone ignored duplicate confirmation sequence start.");
			return;
		}

		if (!CanProceedWithSelectedSupport())
		{
			return;
		}

		_confirmationSequenceRunning = true;
		PublishPhoneVisualPhase(UavPhoneVisualPhase.Confirming, duration: 4.0f);
		_confirmationSequenceCoroutine = StartCoroutine(RunConfirmationSequence());
	}

	private void CancelConfirmationSequenceBeforeCommit()
	{
		if (!_confirmationSequenceRunning || _paymentAttempted || _restoreStarted)
		{
			return;
		}

		if (_confirmationSequenceCoroutine != null)
		{
			StopCoroutine(_confirmationSequenceCoroutine);
			_confirmationSequenceCoroutine = null;
		}

		_confirmationSequenceRunning = false;
		_phoneScreen?.StopConfirmSwipeAnimation();
		ResetPhoneAnimatorSpeed("cancel before payment commit");
		FireSupportPlugin.LogSource.LogInfo("TSC phone confirmation cancelled before payment commit.");
		PublishPhoneVisualPhase(UavPhoneVisualPhase.Cancelled, duration: 0.85f);
		FinishAuthorizationSession(playOutro: true, success: false);
	}

	private IEnumerator RunConfirmationSequence()
	{
		bool handedOffToRestore = false;
		float sequenceStartedAt = Time.unscaledTime;

		try
		{
			_authorizationInputLocked = true;
			_paymentAttempted = false;
			_authorizationGranted = false;
			_restoreStarted = false;
			_phoneScreen?.StopConfirmSwipeAnimation();
			ResetPhoneAnimatorSpeed("confirmation sequence start");

			ESupportType selectedSupportType = _selectedSupportType;
			bool expectedSuccess = FireSupportPayment.CanAfford(selectedSupportType, notify: false);
			LogConfirmSequence("sequence started", sequenceStartedAt, $"expectedPaymentSuccess={expectedSuccess}");

			TscDiagnostics.LogPhone("TSC phone rotate confirmed; using authorization outro pose for portrait payment view.");
			yield return MovePhoneToPortraitOutroPose(expectedSuccess, GetConfirmPortraitTextureDelaySeconds());
			if (!_confirmationSequenceRunning || _finishNotified)
			{
				yield break;
			}

			ShowPhoneState(TerraGroupPhoneState.ConfirmPaymentPortrait);
			LogConfirmSequence("portrait texture shown", sequenceStartedAt);

			int layer = GetHandsLayer(PhoneAnimator);
			float swipeStartNormalizedTime = GetConfirmSwipeStartNormalizedTime();
			float swipeCommitNormalizedTime = Mathf.Max(
				swipeStartNormalizedTime + 0.01f,
				GetConfirmSwipeCommitNormalizedTime());
			yield return WaitForAnimatorNormalizedTime(
				PhoneAnimator,
				layer,
				swipeStartNormalizedTime,
				ConfirmAnimatorWaitTimeoutSeconds,
				"swipe start",
				sequenceStartedAt);
			if (!_confirmationSequenceRunning || _finishNotified)
			{
				yield break;
			}

			SetPhoneAnimatorSpeed(GetConfirmSwipeSpeedMultiplier(), "confirm swipe start reached");
			_phoneScreen?.SetConfirmSwipeAnimationProgress(0f);
			LogConfirmSequence("swipe start reached", sequenceStartedAt, $"targetNormalizedTime={swipeStartNormalizedTime:F3}");
			yield return WaitForAnimatorNormalizedTime(
				PhoneAnimator,
				layer,
				swipeCommitNormalizedTime,
				ConfirmAnimatorWaitTimeoutSeconds,
				"payment commit",
				sequenceStartedAt,
				normalizedTime => _phoneScreen?.SetConfirmSwipeAnimationProgress(
					ComputeSwipeProgress(normalizedTime, swipeStartNormalizedTime, swipeCommitNormalizedTime)));
			if (!_confirmationSequenceRunning || _finishNotified)
			{
				yield break;
			}

			_phoneScreen?.SetConfirmSwipeAnimationProgress(1f);
			_phoneScreen?.StopConfirmSwipeAnimation();
			if (GetConfirmPauseAtCommit())
			{
				SetPhoneAnimatorSpeed(0f, "payment commit pause");
				LogConfirmSequence("animator paused", sequenceStartedAt);
			}

			ShowPhoneState(TerraGroupPhoneState.Authorizing, AuthorizingFadeSeconds);
			LogConfirmSequence("authorizing shown", sequenceStartedAt);
			_paymentAttempted = true;
			LogConfirmSequence("payment commit fired", sequenceStartedAt);
			bool purchaseCompleted = false;
			bool success = false;
			FireSupportPurchaseResponse purchaseResult = null;
			FireSupportPayment.TryPurchaseAuthorizationAsync(
				selectedSupportType,
				notify: false,
				(paid, result) =>
				{
					success = paid;
					purchaseResult = result;
					purchaseCompleted = true;
				});
			while (!purchaseCompleted && _confirmationSequenceRunning && !_finishNotified)
			{
				yield return null;
			}

			if (!_confirmationSequenceRunning || _finishNotified)
			{
				yield break;
			}

			_authorizationGranted = success;
			LogConfirmSequence(
				"payment result",
				sequenceStartedAt,
				$"paid={success}, source={purchaseResult?.PaymentSource ?? "<unknown>"}, reason={purchaseResult?.Reason ?? string.Empty}, serverRevision={purchaseResult?.ServerRevision ?? 0}");
			if (success != expectedSuccess)
			{
				_authorizationOutroPreplayed = false;
				TscDiagnostics.LogPhone(
					$"TSC phone expected payment success changed during confirmation. expected={expectedSuccess}, actual={success}.");
			}

			yield return new WaitForSecondsRealtime(GetAuthorizingDisplaySeconds());

			ShowPhoneState(
				success ? TerraGroupPhoneState.Authorized : TerraGroupPhoneState.Denied,
				AuthorizedFadeSeconds);
			LogConfirmSequence(success ? "authorized shown" : "denied shown", sequenceStartedAt);
			PublishPhoneVisualPhase(
				success ? UavPhoneVisualPhase.Authorized : UavPhoneVisualPhase.Cancelled,
				success,
				0.9f);
			yield return new WaitForSecondsRealtime(AuthorizedFadeSeconds);
			if (success)
			{
				LogConfirmSequence("authorization granted", sequenceStartedAt);
				FireSupportPayment.NotifyAuthorizationPurchased(selectedSupportType);
				LogConfirmSequence("notification shown", sequenceStartedAt);
			}
			else
			{
				FireSupportPayment.NotifyAuthorizationPurchaseDenied(selectedSupportType, purchaseResult);
				LogConfirmSequence("payment denied notification shown", sequenceStartedAt);
			}

			yield return new WaitForSecondsRealtime(success ? GetAuthorizedDisplaySeconds() : GetDeniedDisplaySeconds());
			yield return new WaitForSecondsRealtime(GetRestoreAfterAuthorizedSeconds());

			_restoreStarted = true;
			SetPhoneAnimatorSpeed(GetConfirmOutroSpeedMultiplier(), "result held; resume outro");
			LogConfirmSequence("animator resumed", sequenceStartedAt);
			LogConfirmSequence("restore started", sequenceStartedAt);
			_confirmationSequenceRunning = false;
			_confirmationSequenceCoroutine = null;
			handedOffToRestore = true;
			FinishAuthorizationSession(playOutro: !_authorizationOutroPreplayed, success: success);
		}
		finally
		{
			if (!handedOffToRestore)
			{
				_phoneScreen?.StopConfirmSwipeAnimation();
				ResetPhoneAnimatorSpeed("confirmation sequence cleanup");
			}
		}
	}

	private IEnumerator WaitForAnimatorNormalizedTime(
		Animator animator,
		int layer,
		float target,
		float timeoutSeconds,
		string phaseName,
		float sequenceStartedAt,
		Action<float> normalizedTick = null)
	{
		float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
		while (_confirmationSequenceRunning &&
		       !_finishNotified &&
		       Time.unscaledTime < deadline)
		{
			if (animator == null)
			{
				yield break;
			}

			AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
			if (IsAuthorizationOutroState(state))
			{
				float normalizedTime = state.normalizedTime;
				normalizedTick?.Invoke(normalizedTime);
				if (normalizedTime >= target)
				{
					LogConfirmSequence(
						$"{phaseName} reached",
						sequenceStartedAt,
						$"targetNormalizedTime={target:F3}, actualNormalizedTime={normalizedTime:F3}");
					yield break;
				}
			}

			yield return null;
		}

		FireSupportPlugin.LogSource.LogWarning(
			$"TerraGroup phone confirm sequence timed out waiting for {phaseName} normalizedTime={target:F3}. {DescribeAnimatorState(animator, null)}");
	}

	private void SetPhoneAnimatorSpeed(float speed, string reason)
	{
		if (PhoneAnimator == null)
		{
			return;
		}

		PhoneAnimator.speed = Mathf.Max(0f, speed);
		_phoneAnimatorSpeedCustomized = true;
		TscDiagnostics.LogPhone($"TSC phone animator speed={PhoneAnimator.speed:F2}, reason={reason}.");
	}

	private void ResetPhoneAnimatorSpeed(string reason)
	{
		if (PhoneAnimator == null)
		{
			return;
		}

		if (_phoneAnimatorSpeedCustomized || !Mathf.Approximately(PhoneAnimator.speed, 1f))
		{
			PhoneAnimator.speed = 1f;
			_phoneAnimatorSpeedCustomized = false;
			TscDiagnostics.LogPhone($"TSC phone animator speed reset to 1.00, reason={reason}.");
		}
	}

	private static float ComputeSwipeProgress(float normalizedTime, float start, float commit)
	{
		if (commit <= start + 0.0001f)
		{
			return 1f;
		}

		return Mathf.Clamp01((normalizedTime - start) / (commit - start));
	}

	private IEnumerator MovePhoneToPortraitOutroPose(bool success, float portraitPoseSeconds)
	{
		_authorizationOutroPreplayed = false;
		if (PhoneAnimator == null)
		{
			yield break;
		}

		string stateName = success ? "Outro Success" : "Outro Fail";
		int layer = GetHandsLayer(PhoneAnimator);
		try
		{
			ResetPhoneAnimatorSpeed("portrait outro start");
			PhoneAnimator.SetBool("Active", false);
			PhoneAnimator.Play(stateName, layer, 0f);
			_authorizationOutroPreplayed = true;
			TscDiagnostics.LogPhone($"TSC phone portrait pose animation started: {stateName}.");
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone portrait pose animation failed. {ex}");
			yield break;
		}

		float startedAt = Time.unscaledTime;
		float stop = startedAt + PortraitOutroPoseTimeoutSeconds;
		while (Time.unscaledTime < stop)
		{
			AnimatorStateInfo info = PhoneAnimator.GetCurrentAnimatorStateInfo(layer);
			if (Time.unscaledTime - startedAt >= portraitPoseSeconds &&
			    IsAuthorizationOutroState(info))
			{
				yield break;
			}

			yield return null;
		}
	}

	private void LogConfirmSequence(string eventName, float sequenceStartedAt, string extra = null)
	{
		if (!TscDiagnostics.VerbosePhone)
		{
			return;
		}

		string animatorState = "<null>";
		float normalizedTime = 0f;
		if (PhoneAnimator != null)
		{
			AnimatorStateInfo info = PhoneAnimator.GetCurrentAnimatorStateInfo(GetHandsLayer(PhoneAnimator));
			animatorState = GetKnownAnimatorStateName(info);
			normalizedTime = info.normalizedTime;
		}

		string suffix = string.IsNullOrWhiteSpace(extra) ? string.Empty : $", {extra}";
		FireSupportPlugin.LogSource.LogInfo(
			$"TSC phone confirm sequence: event={eventName}, currentState={_phoneState}, selectedProduct={_selectedSupportType}, animatorState={animatorState}, animatorNormalizedTime={normalizedTime:F3}, elapsed={Time.unscaledTime - sequenceStartedAt:F3}, paymentAttempted={_paymentAttempted}, authorizationGranted={_authorizationGranted}{suffix}.");
	}

	private static string GetKnownAnimatorStateName(AnimatorStateInfo info)
	{
		if (info.IsName("Hands.Outro Success") || info.IsName("Outro Success"))
		{
			return "Outro Success";
		}

		if (info.IsName("Hands.Outro Fail") || info.IsName("Outro Fail"))
		{
			return "Outro Fail";
		}

		if (info.IsName("Hands.Idle_Loop") || info.IsName("Idle_Loop"))
		{
			return "Idle_Loop";
		}

		if (info.IsName("Hands.Tap") || info.IsName("Tap"))
		{
			return "Tap";
		}

		if (info.IsName("Hands.Spawn") || info.IsName("Spawn"))
		{
			return "Spawn";
		}

		return $"hash:{info.fullPathHash}/{info.shortNameHash}";
	}

	private static float GetConfirmPortraitTextureDelaySeconds()
	{
		return Mathf.Clamp(PluginSettings.PhoneConfirmPortraitTextureDelaySeconds?.Value ?? 0.45f, 0f, 3f);
	}

	private static float GetConfirmSwipeSpeedMultiplier()
	{
		return Mathf.Clamp(PluginSettings.PhoneConfirmSwipeSpeedMultiplier?.Value ?? 1.4f, 0.25f, 4f);
	}

	private static float GetConfirmSwipeStartNormalizedTime()
	{
		return Mathf.Clamp01(PluginSettings.PhoneConfirmSwipeStartNormalizedTime?.Value ?? 0.36f);
	}

	private static float GetConfirmSwipeCommitNormalizedTime()
	{
		return Mathf.Clamp01(PluginSettings.PhoneConfirmSwipeCommitNormalizedTime?.Value ?? 0.78f);
	}

	private static bool GetConfirmPauseAtCommit()
	{
		return PluginSettings.PhoneConfirmPauseAtCommit?.Value ?? true;
	}

	private static float GetConfirmOutroSpeedMultiplier()
	{
		return Mathf.Clamp(PluginSettings.PhoneConfirmOutroSpeedMultiplier?.Value ?? 1.25f, 0.25f, 4f);
	}

	private static float GetAuthorizingDisplaySeconds()
	{
		return Mathf.Clamp(PluginSettings.PhoneAuthorizingDisplaySeconds?.Value ?? 0.55f, 0.1f, 5f);
	}

	private static float GetAuthorizedDisplaySeconds()
	{
		return Mathf.Clamp(PluginSettings.PhoneAuthorizedDisplaySeconds?.Value ?? 0.85f, 0.1f, 5f);
	}

	private static float GetDeniedDisplaySeconds()
	{
		return Mathf.Clamp(PluginSettings.PhoneDeniedDisplaySeconds?.Value ?? 0.85f, 0.1f, 5f);
	}

	private static float GetRestoreAfterAuthorizedSeconds()
	{
		return Mathf.Clamp(PluginSettings.PhoneRestoreAfterAuthorizedSeconds?.Value ?? 0.15f, 0f, 3f);
	}

	private void SelectSupportType(ESupportType supportType)
	{
		if (_selectedSupportType == supportType)
		{
			return;
		}

		_selectedSupportType = supportType;
		ShowPhoneState(_phoneState);
	}

	private bool CanProceedWithSelectedSupport()
	{
		if (FireSupportServiceAvailability.IsServiceEnabled(_selectedSupportType))
		{
			return true;
		}

		TscDiagnostics.LogPhone($"TSC phone blocked disabled selected service: {_selectedSupportType}.");
		FireSupportPayment.NotifyServiceUnavailable(_selectedSupportType);
		return false;
	}

	private void ShowPhoneState(TerraGroupPhoneState state)
	{
		_phoneState = state;
		_phoneScreen?.Rebuild(BuildPhoneContext(), state);
	}

	private void ShowPhoneState(TerraGroupPhoneState state, float fadeSeconds)
	{
		_phoneState = state;
		_phoneScreen?.FadeToState(state, fadeSeconds);
	}

	private UavPhoneScreenContext BuildPhoneContext()
	{
		return new UavPhoneScreenContext(
			_selectedSupportType,
			FireSupportPayment.GetActiveCost(_selectedSupportType),
			FireSupportPayment.GetEffectiveBalance(),
			UavReconSettings.GetDurationSeconds(_selectedSupportType));
	}

	private void FinishAuthorizationSession(bool playOutro, bool success)
	{
		if (_finishNotified)
		{
			return;
		}

		_authorizationSessionActive = false;
		_authorizationInputLocked = true;
		_restoreStarted = true;
		_confirmationSequenceRunning = false;
		_confirmationSequenceCoroutine = null;
		_phoneScreen?.StopConfirmSwipeAnimation();

		if (playOutro)
		{
			if (success)
			{
				PlayOutroSuccess();
			}
			else
			{
				PlayOutroFail();
			}
		}

		if (!success && !_phoneVisualTerminalPhaseSent)
		{
			PublishPhoneVisualPhase(UavPhoneVisualPhase.Cancelled, duration: 0.85f);
		}

		PublishPhoneVisualPhase(UavPhoneVisualPhase.End, success, 0.35f);

		try
		{
			_onUsedCallback?.Invoke(new Result<IOnHandsUseCallback>(this));
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone authorization quick-use callback failed. {ex}");
		}

		NotifyAuthorizationFinished(success);
	}

	private void PublishPhoneVisualPhase(UavPhoneVisualPhase phase, bool success = false, float duration = 0f)
	{
		if (phase == UavPhoneVisualPhase.Authorized || phase == UavPhoneVisualPhase.Cancelled)
		{
			_phoneVisualTerminalPhaseSent = true;
		}

		UavPhoneVisualNetworkService.PublishLocal(
			_selectedSupportType,
			phase,
			duration,
			success);
	}

	private void NotifyAuthorizationFinished(bool success)
	{
		if (_finishNotified)
		{
			return;
		}

		_finishNotified = true;
		_finishPending = true;
		_finishSuccess = success;

		try
		{
			_authorizationSessionFinished?.Invoke(this, success);
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone authorization finish callback failed. {ex}");
		}
	}

	public IEnumerator WaitForAuthorizationOutro(float timeoutSeconds = 1.7f)
	{
		if (PhoneAnimator == null)
		{
			yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, timeoutSeconds));
			yield break;
		}

		int layer = GetHandsLayer(PhoneAnimator);
		float stop = Time.unscaledTime + timeoutSeconds;
		while (Time.unscaledTime < stop)
		{
			AnimatorStateInfo info = PhoneAnimator.GetCurrentAnimatorStateInfo(layer);
			if (IsAuthorizationOutroState(info) &&
			    info.normalizedTime >= 0.95f)
			{
				ResetPhoneAnimatorSpeed("authorization outro complete");
				yield break;
			}

			yield return null;
		}

		ResetPhoneAnimatorSpeed("authorization outro wait timeout");
	}

	public void ShutdownPhoneScreenForExternalRestore()
	{
		ResetPhoneAnimatorSpeed("external restore");
		ShutdownPhoneScreen();
	}

	private void ShutdownPhoneScreen()
	{
		if (_phoneScreen == null)
		{
			return;
		}

		try
		{
			_phoneScreen.Shutdown();
		}
		catch (Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"TerraGroup phone UI shutdown failed. {ex}");
		}

		Destroy(_phoneScreen);
		_phoneScreen = null;
	}

	private IEnumerator LogAnimatorStartup(WeaponPrefab weaponPrefab)
	{
		Renderer screenRenderer = FindScreenRenderer(weaponPrefab?.transform);
		float stop = Time.unscaledTime + StartupAnimatorLogSeconds;
		while (Time.unscaledTime <= stop)
		{
			TscDiagnostics.LogPhone($"TSC phone animator startup: {DescribeAnimatorState(PhoneAnimator, screenRenderer)}");
			yield return new WaitForSecondsRealtime(StartupAnimatorLogIntervalSeconds);
		}
	}

	private static int GetHandsLayer(Animator animator)
	{
		return animator != null && animator.layerCount > HandsLayer ? HandsLayer : 0;
	}

	private static bool IsIdleLoop(Animator animator)
	{
		if (animator == null)
		{
			return false;
		}

		AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(GetHandsLayer(animator));
		return info.IsName("Hands.Idle_Loop") || info.IsName("Idle_Loop");
	}

	private static bool IsAuthorizationOutroState(AnimatorStateInfo info)
	{
		return info.IsName("Outro Success") || info.IsName("Hands.Outro Success") ||
		       info.IsName("Outro Fail") || info.IsName("Hands.Outro Fail");
	}

	private static string DescribeAnimatorState(Animator animator, Renderer screenRenderer)
	{
		if (animator == null)
		{
			return "animator=<null>";
		}

		int layer = GetHandsLayer(animator);
		AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(layer);
		Vector3 phonePosition = animator.transform.position;
		Vector3 screenCenter = screenRenderer == null ? Vector3.zero : screenRenderer.bounds.center;
		return
			$"layer={layer}, fullHash={info.fullPathHash}, shortHash={info.shortNameHash}, " +
			$"Hands.Spawn={info.IsName("Hands.Spawn")}, Spawn={info.IsName("Spawn")}, " +
			$"Hands.Equip={info.IsName("Hands.Equip")}, Equip={info.IsName("Equip")}, " +
			$"Hands.Idle_Loop={info.IsName("Hands.Idle_Loop")}, Idle_Loop={info.IsName("Idle_Loop")}, " +
			$"normalizedTime={info.normalizedTime:F3}, layerWeight={animator.GetLayerWeight(layer):F3}, " +
			$"phonePos=({phonePosition.x:F3},{phonePosition.y:F3},{phonePosition.z:F3}), " +
			$"screenCenter={(screenRenderer == null ? "<null>" : $"({screenCenter.x:F3},{screenCenter.y:F3},{screenCenter.z:F3})")}";
	}

	private static Renderer FindScreenRenderer(Transform root)
	{
		Transform screen = FindChildByName(root, "Screen");
		return screen == null ? null : screen.GetComponent<Renderer>();
	}

	private static Transform FindChildByName(Transform root, string name)
	{
		if (root == null)
		{
			return null;
		}

		foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
		{
			if (child.name == name)
			{
				return child;
			}
		}

		return null;
	}

	private static string DescribeItem(Item item)
	{
		if (item == null)
		{
			return "item=<null>";
		}

		try
		{
			string parentId = item.Template?.ParentId?.ToString() ?? "<null>";
			string prefabPath = item.Prefab.FileName ?? item.Template?.Prefab.FileName ?? "<null>";
			string usePrefabPath = item.UsePrefab.FileName ?? item.Template?.UsePrefab.FileName ?? "<null>";

			return
				$"item={item.Id}, tpl={item.StringTemplateId}, parent={parentId}, type={item.GetType().FullName}, prefab={prefabPath}, usePrefab={usePrefabPath}";
		}
		catch (Exception ex)
		{
			return $"itemDiagnosticsFailed={ex.GetType().Name}:{ex.Message}";
		}
	}

	private void OnDestroy()
	{
		ResetPhoneAnimatorSpeed("controller destroy");
		ShutdownPhoneScreen();
	}

	private System.Collections.IEnumerator PlayTapAudioAfter(float delay)
	{
		yield return new WaitForSecondsRealtime(delay);

		if (_tapAudioSource?.clip == null)
		{
			yield break;
		}

		try
		{
			_tapAudioSource.PlayOneShot(_tapAudioSource.clip);
		}
		catch (System.Exception ex)
		{
			FireSupportPlugin.LogSource.LogWarning($"UAV activation device tap audio failed. {ex}");
		}
	}
}
