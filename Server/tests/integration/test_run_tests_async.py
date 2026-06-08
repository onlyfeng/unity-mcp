import pytest

from .test_helpers import DummyContext


def _retryable_get_test_job_timeout():
    return {
        "success": False,
        "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
        "hint": "retry",
    }


@pytest.mark.asyncio
async def test_run_tests_async_forwards_params(monkeypatch):
    from services.tools.run_tests import run_tests

    captured = {}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        captured["command_type"] = command_type
        captured["params"] = params
        return {"success": True, "data": {"job_id": "abc123", "status": "running", "mode": "EditMode"}}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await run_tests(
        DummyContext(),
        mode="EditMode",
        test_names="MyNamespace.MyTests.TestA",
        include_details=True,
    )
    assert captured["command_type"] == "run_tests"
    assert captured["params"]["mode"] == "EditMode"
    assert captured["params"]["testNames"] == ["MyNamespace.MyTests.TestA"]
    assert captured["params"]["includeDetails"] is True
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.job_id == "abc123"


@pytest.mark.asyncio
async def test_run_tests_forwards_init_timeout(monkeypatch):
    from services.tools.run_tests import run_tests

    captured = {}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"job_id": "abc123", "status": "running", "mode": "PlayMode"}}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await run_tests(
        DummyContext(),
        mode="PlayMode",
        init_timeout=120000,
    )
    assert captured["params"]["initTimeout"] == 120000
    assert resp.success is True


@pytest.mark.asyncio
async def test_run_tests_omits_init_timeout_when_none(monkeypatch):
    from services.tools.run_tests import run_tests

    captured = {}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        captured["params"] = params
        return {"success": True, "data": {"job_id": "abc123", "status": "running", "mode": "EditMode"}}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await run_tests(DummyContext(), mode="EditMode")
    assert "initTimeout" not in captured["params"]
    assert resp.success is True


@pytest.mark.asyncio
async def test_run_tests_rejects_negative_init_timeout():
    from services.tools.run_tests import run_tests

    resp = await run_tests(DummyContext(), mode="EditMode", init_timeout=-1)
    assert resp.success is False
    assert "init_timeout" in resp.error


@pytest.mark.asyncio
async def test_run_tests_rejects_zero_init_timeout():
    from services.tools.run_tests import run_tests

    resp = await run_tests(DummyContext(), mode="EditMode", init_timeout=0)
    assert resp.success is False
    assert "init_timeout" in resp.error


@pytest.mark.asyncio
async def test_get_test_job_forwards_job_id(monkeypatch):
    from services.tools.run_tests import get_test_job

    captured = {}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        captured["command_type"] = command_type
        captured["params"] = params
        return {"success": True, "data": {"job_id": params["job_id"], "status": "running", "mode": "EditMode"}}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    resp = await get_test_job(DummyContext(), job_id="job-1")
    assert captured["command_type"] == "get_test_job"
    assert captured["params"]["job_id"] == "job-1"
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.job_id == "job-1"


@pytest.mark.asyncio
async def test_get_test_job_returns_cached_status_after_transport_retry(monkeypatch):
    from services.tools.run_tests import get_test_job, run_tests

    calls = []

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        calls.append(command_type)
        if command_type == "run_tests":
            return {
                "success": True,
                "data": {
                    "job_id": "job-cached",
                    "status": "running",
                    "mode": "EditMode",
                },
            }
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-cached")
    assert calls == ["run_tests", "get_test_job"]
    assert resp.success is True
    assert resp.hint == "retry"
    assert resp.data is not None
    assert resp.data.job_id == "job-cached"
    assert resp.data.status == "running"
    assert resp.data.transport_degraded is True
    assert "did not respond" in resp.data.transport_error


@pytest.mark.asyncio
async def test_get_test_job_degraded_response_reports_transport_stall(monkeypatch):
    from services.tools.run_tests import get_test_job, run_tests
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {
                "success": True,
                "data": {
                    "job_id": "job-stalled",
                    "status": "running",
                    "mode": "EditMode",
                    "last_update_unix_ms": 100_000,
                },
            }
        return _retryable_get_test_job_timeout()

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    monkeypatch.setattr(mod.time, "time", lambda: 130.5)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-stalled")
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.transport_degraded is True
    assert resp.data.cached_unix_ms == 130_500
    assert resp.data.server_observed_unix_ms == 130_500
    assert resp.data.transport_stall_ms == 30_500
    assert resp.data.server_stuck_suspected is True
    assert resp.data.progress is not None
    assert resp.data.progress.stuck_suspected is True
    assert resp.data.progress.blocked_reason == "unity_transport_unresponsive"


@pytest.mark.asyncio
async def test_get_test_job_degraded_response_uses_cached_time_without_last_update(monkeypatch):
    from services.tools.run_tests import get_test_job, run_tests
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()
    ticks = iter([100.0, 130.25])

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {
                "success": True,
                "data": {
                    "job_id": "job-no-last-update",
                    "status": "running",
                    "mode": "EditMode",
                },
            }
        return _retryable_get_test_job_timeout()

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    monkeypatch.setattr(mod.time, "time", lambda: next(ticks))

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-no-last-update")
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.cached_unix_ms == 100_000
    assert resp.data.server_observed_unix_ms == 130_250
    assert resp.data.transport_stall_ms == 30_250
    assert resp.data.server_stuck_suspected is True
    assert resp.data.progress is not None
    assert resp.data.progress.blocked_reason == "unity_transport_unresponsive"


@pytest.mark.asyncio
async def test_get_test_job_degraded_response_preserves_specific_blocked_reason(monkeypatch):
    from services.tools.run_tests import get_test_job, run_tests
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {
                "success": True,
                "data": {
                    "job_id": "job-specific-reason",
                    "status": "running",
                    "mode": "EditMode",
                    "last_update_unix_ms": 100_000,
                    "progress": {"blocked_reason": "domain_reload"},
                },
            }
        return _retryable_get_test_job_timeout()

    monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    monkeypatch.setattr(mod.time, "time", lambda: 130.0)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-specific-reason")
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.server_stuck_suspected is True
    assert resp.data.progress is not None
    assert resp.data.progress.stuck_suspected is True
    assert resp.data.progress.blocked_reason == "domain_reload"


@pytest.mark.asyncio
async def test_get_test_job_real_error_not_masked_by_cache(monkeypatch):
    """A genuine tool error (e.g. expired job id) must surface even when a
    cached snapshot exists, instead of being hidden behind a retry hint."""
    from services.tools.run_tests import get_test_job, run_tests

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {
                "success": True,
                "data": {
                    "job_id": "job-expired",
                    "status": "running",
                    "mode": "EditMode",
                },
            }
        return {"success": False, "error": "Unknown job_id."}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-expired")
    assert resp.success is False
    assert resp.error == "Unknown job_id."


@pytest.mark.asyncio
async def test_get_test_job_wait_keeps_polling_through_transient_failure(monkeypatch):
    """With wait_timeout set, a transient transport hiccup must not bounce the
    caller; the wait loop should keep polling until a terminal status."""
    from services.tools.run_tests import get_test_job, run_tests

    poll = {"n": 0}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {"success": True, "data": {"job_id": "job-wait", "status": "running", "mode": "EditMode"}}
        poll["n"] += 1
        if poll["n"] == 1:
            return {
                "success": False,
                "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
                "hint": "retry",
            }
        return {"success": True, "data": {"job_id": "job-wait", "status": "succeeded", "mode": "EditMode"}}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    # Avoid the real 2s poll interval.
    async def _instant_sleep(_seconds):
        return None
    monkeypatch.setattr(mod.asyncio, "sleep", _instant_sleep)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-wait", wait_timeout=5)
    assert poll["n"] == 2  # kept polling past the transient failure
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.status == "succeeded"
    assert resp.data.transport_degraded is None


@pytest.mark.asyncio
async def test_get_test_job_wait_falls_back_to_cache_at_deadline(monkeypatch):
    """When the deadline is reached while Unity stays unreachable, the wait loop
    serves the last cached snapshot flagged as degraded."""
    from services.tools.run_tests import get_test_job, run_tests

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {"success": True, "data": {"job_id": "job-deadline", "status": "running", "mode": "EditMode"}}
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    async def _instant_sleep(_seconds):
        return None
    monkeypatch.setattr(mod.asyncio, "sleep", _instant_sleep)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-deadline", wait_timeout=0.01)
    assert resp.success is True
    assert resp.hint == "retry"
    assert resp.data is not None
    assert resp.data.status == "running"
    assert resp.data.transport_degraded is True


@pytest.mark.asyncio
async def test_get_test_job_unity_side_command_timeout_is_retryable(monkeypatch):
    """A Unity-side dispatcher timeout (success=False, no hint, 'timed out after'
    message) must be treated as a transient transport timeout and serve the
    cached snapshot, not surfaced as a hard failure."""
    from services.tools.run_tests import get_test_job, run_tests

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if command_type == "run_tests":
            return {"success": True, "data": {"job_id": "job-busy", "status": "running", "mode": "EditMode"}}
        # No hint, and message differs from the server-side fast-fail text.
        return {"success": False, "error": "Command 'get_test_job' timed out after 2 seconds"}

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    start = await run_tests(DummyContext(), mode="EditMode")
    assert start.success is True

    resp = await get_test_job(DummyContext(), job_id="job-busy")
    assert resp.success is True
    assert resp.hint == "retry"
    assert resp.data is not None
    assert resp.data.status == "running"
    assert resp.data.transport_degraded is True
    assert "timed out after" in resp.data.transport_error


@pytest.mark.asyncio
async def test_get_test_job_wait_returns_cached_terminal_without_waiting(monkeypatch):
    """If a terminal snapshot is already cached, a later transient poll must
    return it immediately rather than blocking until wait_timeout expires."""
    from services.tools.run_tests import get_test_job

    sleeps = []

    async def _record_sleep(seconds):
        sleeps.append(seconds)

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        # First poll reports terminal; a subsequent poll hits a transport stall.
        if not poll["seen_terminal"]:
            poll["seen_terminal"] = True
            return {"success": True, "data": {"job_id": "job-done", "status": "succeeded", "mode": "EditMode"}}
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    poll = {"seen_terminal": False}
    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)
    monkeypatch.setattr(mod.asyncio, "sleep", _record_sleep)

    # First call caches the terminal snapshot (returns succeeded).
    first = await get_test_job(DummyContext(), job_id="job-done")
    assert first.data.status == "succeeded"

    # Second call with a long wait hits a transient stall but must return the
    # cached terminal status immediately, never sleeping.
    resp = await get_test_job(DummyContext(), job_id="job-done", wait_timeout=60)
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.status == "succeeded"
    assert resp.data.transport_degraded is None
    assert resp.hint is None
    assert sleeps == []  # returned without waiting out the timeout


@pytest.mark.asyncio
async def test_terminal_cache_not_authoritative_for_higher_detail(monkeypatch):
    """A terminal snapshot cached from a no-detail poll must NOT be served to a
    later include_details poll that hits a transport stall (Unity's payload
    differs and the cache lacks the requested results). The caller should get a
    plain retry, not a (degraded) terminal status missing the detail it asked
    for, so it polls again once Unity responds."""
    from services.tools.run_tests import get_test_job

    n = {"poll": 0}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        n["poll"] += 1
        if n["poll"] == 1:
            # First poll (no detail flags) reports terminal without results.
            return {"success": True, "data": {"job_id": "job-d", "status": "succeeded", "mode": "EditMode"}}
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    first = await get_test_job(DummyContext(), job_id="job-d")
    assert first.data.status == "succeeded"

    # Now ask for full details; the cached terminal snapshot lacks them, so we
    # must neither short-circuit with the clean terminal cache nor serve a
    # degraded terminal without the requested results — surface a plain retry.
    resp = await get_test_job(DummyContext(), job_id="job-d", include_details=True)
    assert resp.success is False
    assert resp.hint == "retry"
    assert resp.data is None


@pytest.mark.asyncio
async def test_terminal_cache_authoritative_when_detail_matches(monkeypatch):
    """When the cached terminal snapshot was fetched with the same detail level,
    a later transient poll can be served from cache immediately."""
    from services.tools.run_tests import get_test_job

    n = {"poll": 0}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        n["poll"] += 1
        if n["poll"] == 1:
            assert params.get("includeDetails") is True
            return {"success": True, "data": {"job_id": "job-d2", "status": "succeeded", "mode": "EditMode"}}
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    first = await get_test_job(DummyContext(), job_id="job-d2", include_details=True)
    assert first.data.status == "succeeded"

    resp = await get_test_job(DummyContext(), job_id="job-d2", include_details=True)
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.status == "succeeded"
    assert resp.data.transport_degraded is None
    assert resp.hint is None


@pytest.mark.asyncio
async def test_low_detail_poll_does_not_clobber_high_detail_terminal(monkeypatch):
    """A high-detail terminal snapshot must survive a later low-detail poll of
    the same job, so a subsequent high-detail request can still be served from
    cache during a transport stall."""
    from services.tools.run_tests import get_test_job

    n = {"poll": 0}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        n["poll"] += 1
        if n["poll"] in (1, 2):
            # Poll 1 (high detail) and poll 2 (low detail) both report terminal.
            return {"success": True, "data": {"job_id": "job-d3", "status": "succeeded", "mode": "EditMode"}}
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    import services.tools.run_tests as mod
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    # Cache a high-detail terminal snapshot, then a low-detail poll of same job.
    await get_test_job(DummyContext(), job_id="job-d3", include_details=True)
    await get_test_job(DummyContext(), job_id="job-d3")  # must NOT clobber

    # A high-detail request during a stall is still served from the preserved
    # high-detail terminal snapshot (clean, not degraded).
    resp = await get_test_job(DummyContext(), job_id="job-d3", include_details=True)
    assert resp.success is True
    assert resp.data is not None
    assert resp.data.status == "succeeded"
    assert resp.data.transport_degraded is None
    assert resp.hint is None


@pytest.mark.asyncio
async def test_protected_terminal_snapshot_refreshes_lru_recency(monkeypatch):
    """When a low-detail poll is prevented from clobbering a richer terminal
    snapshot, that snapshot's LRU recency must be refreshed so it isn't evicted
    prematurely — otherwise the protection is undone on the next poll."""
    from services.tools.run_tests import get_test_job
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        job_id = params.get("job_id")
        return {"success": True, "data": {"job_id": job_id, "status": "succeeded", "mode": "EditMode"}}

    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    # A: high-detail terminal, then B: terminal -> cache order [A, B]
    await get_test_job(DummyContext(), job_id="A", include_details=True)
    await get_test_job(DummyContext(), job_id="B", include_details=True)
    assert list(mod._test_job_status_cache) == [("default", "A"), ("default", "B")]

    # Low-detail poll of A must not clobber its richer snapshot, and must move it
    # to the most-recent position -> [B, A].
    await get_test_job(DummyContext(), job_id="A")
    assert list(mod._test_job_status_cache) == [("default", "B"), ("default", "A")]
    assert mod._test_job_status_cache[("default", "A")]["details"] is True  # richer snapshot kept


@pytest.mark.asyncio
async def test_cached_test_jobs_are_scoped_by_unity_instance(monkeypatch):
    """A retryable failure for one Unity instance must not serve another
    instance's cached payload just because the job_id matches."""
    from services.tools.run_tests import get_test_job
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if unity_instance == "ProjectA@aaaa":
            return {
                "success": True,
                "data": {
                    "job_id": params["job_id"],
                    "status": "running",
                    "mode": "EditMode",
                    "instance": "A",
                },
            }
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    ctx_a = DummyContext()
    await ctx_a.set_state("unity_instance", "ProjectA@aaaa")
    ctx_b = DummyContext()
    await ctx_b.set_state("unity_instance", "ProjectB@bbbb")

    first = await get_test_job(ctx_a, job_id="same-job")
    assert first.success is True
    assert first.data is not None
    assert first.data.status == "running"

    second = await get_test_job(ctx_b, job_id="same-job")
    assert second.success is False
    assert second.hint == "retry"
    assert second.data is None


@pytest.mark.asyncio
async def test_cached_test_jobs_survive_session_reconnect_for_same_instance(monkeypatch):
    """The cache scope must not use websocket session ids: they change when
    Unity reconnects during domain reload, while the test job still belongs to
    the same Unity instance."""
    from services.tools.run_tests import get_test_job
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()
    calls = {"n": 0}

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        calls["n"] += 1
        if calls["n"] == 1:
            return {
                "success": True,
                "data": {
                    "job_id": params["job_id"],
                    "status": "running",
                    "mode": "EditMode",
                },
            }
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    ctx_before_reload = DummyContext()
    await ctx_before_reload.set_state("user_id", "user-1")
    await ctx_before_reload.set_state("unity_instance", "Project@aaaa")
    await ctx_before_reload.set_state("unity_session_id", "session-before")

    ctx_after_reload = DummyContext()
    await ctx_after_reload.set_state("user_id", "user-1")
    await ctx_after_reload.set_state("unity_instance", "Project@aaaa")
    await ctx_after_reload.set_state("unity_session_id", "session-after")

    first = await get_test_job(ctx_before_reload, job_id="job-reload")
    assert first.success is True
    assert first.data is not None
    assert first.data.status == "running"

    second = await get_test_job(ctx_after_reload, job_id="job-reload")
    assert second.success is True
    assert second.hint == "retry"
    assert second.data is not None
    assert second.data.status == "running"
    assert second.data.transport_degraded is True


@pytest.mark.asyncio
async def test_cached_test_jobs_are_scoped_by_user_for_same_instance(monkeypatch):
    """Remote-hosted callers sharing a Unity instance must not receive each
    other's cached test payloads."""
    from services.tools.run_tests import get_test_job
    import services.tools.run_tests as mod

    mod._test_job_status_cache.clear()

    async def fake_send_with_unity_instance(send_fn, unity_instance, command_type, params, **kwargs):
        if user["id"] == "user-a":
            return {
                "success": True,
                "data": {
                    "job_id": params["job_id"],
                    "status": "running",
                    "mode": "EditMode",
                },
            }
        return {
            "success": False,
            "error": "Unity did not respond to 'get_test_job' within 2.0s; please retry",
            "hint": "retry",
        }

    user = {"id": "user-a"}
    monkeypatch.setattr(
        mod.unity_transport, "send_with_unity_instance", fake_send_with_unity_instance)

    ctx_a = DummyContext()
    await ctx_a.set_state("user_id", "user-a")
    await ctx_a.set_state("unity_instance", "Shared@aaaa")
    ctx_b = DummyContext()
    await ctx_b.set_state("user_id", "user-b")
    await ctx_b.set_state("unity_instance", "Shared@aaaa")

    first = await get_test_job(ctx_a, job_id="same-job")
    assert first.success is True

    user["id"] = "user-b"
    second = await get_test_job(ctx_b, job_id="same-job")
    assert second.success is False
    assert second.hint == "retry"
    assert second.data is None
