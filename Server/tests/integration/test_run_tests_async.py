import pytest

from .test_helpers import DummyContext


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
