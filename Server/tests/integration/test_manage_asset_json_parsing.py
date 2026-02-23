"""
Tests for JSON string parameter parsing in manage_asset tool.
"""

import pytest
import json

from .test_helpers import DummyContext
from services.tools.manage_asset import manage_asset


class TestManageAssetJsonParsing:
    """Test JSON string parameter parsing functionality."""

    @pytest.mark.asyncio
    async def test_properties_json_string_parsing(self, monkeypatch):
        """Test that JSON string properties are correctly parsed to dict."""
        # Mock context
        ctx = DummyContext()

        # Patch Unity transport
        async def fake_async(cmd, params, **kwargs):
            return {
                "success": True,
                "message": "Asset created successfully",
                "data": {"path": "Assets/Test.mat"},
            }

        monkeypatch.setattr(
            "services.tools.manage_asset.async_send_command_with_retry", fake_async
        )

        # Test with JSON string properties
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties='{"shader": "Universal Render Pipeline/Lit", "color": [0, 0, 1, 1]}',
        )

        # Verify the result - JSON string was successfully parsed and passed to Unity
        assert result["success"] is True
        assert "Asset created successfully" in result["message"]

    @pytest.mark.asyncio
    async def test_properties_invalid_json_string(self, monkeypatch):
        """Test handling of invalid JSON string properties."""
        ctx = DummyContext()

        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}

        monkeypatch.setattr(
            "services.tools.manage_asset.async_send_command_with_retry", fake_async
        )

        # Test with invalid JSON string
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties='{"invalid": json, "missing": quotes}',
        )

        # Verify behavior: parsing fails with a clear error
        assert result.get("success") is False
        assert "properties must be" in result.get("message", "")

    @pytest.mark.asyncio
    async def test_properties_dict_unchanged(self, monkeypatch):
        """Test that dict properties are passed through unchanged."""
        ctx = DummyContext()

        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}

        monkeypatch.setattr(
            "services.tools.manage_asset.async_send_command_with_retry", fake_async
        )

        # Test with dict properties
        properties_dict = {
            "shader": "Universal Render Pipeline/Lit",
            "color": [0, 0, 1, 1],
        }

        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties=properties_dict,
        )

        # Verify no JSON parsing was attempted (allow initial Processing log)
        assert not any("coerced properties" in msg for msg in ctx.log_info)
        assert result["success"] is True

    @pytest.mark.asyncio
    async def test_properties_none_handling(self, monkeypatch):
        """Test that None properties are handled correctly."""
        ctx = DummyContext()

        async def fake_async(cmd, params, **kwargs):
            return {"success": True, "message": "Asset created successfully"}

        monkeypatch.setattr(
            "services.tools.manage_asset.async_send_command_with_retry", fake_async
        )

        # Test with None properties
        result = await manage_asset(
            ctx=ctx,
            action="create",
            path="Assets/Test.mat",
            asset_type="Material",
            properties=None,
        )

        # Verify no JSON parsing was attempted (allow initial Processing log)
        assert not any("coerced properties" in msg for msg in ctx.log_info)
        assert result["success"] is True
