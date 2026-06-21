"""Tests for manage_prefabs tool."""

import asyncio
import inspect
from types import SimpleNamespace
from unittest.mock import AsyncMock

import pytest

from services.tools.manage_prefabs import manage_prefabs
from services.registry import get_registered_tools


# ── Fixture ──────────────────────────────────────────────────────────


@pytest.fixture
def mock_unity(monkeypatch):
    captured: dict[str, object] = {}

    async def fake_send(send_fn, unity_instance, tool_name, params):
        captured["unity_instance"] = unity_instance
        captured["tool_name"] = tool_name
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(
        "services.tools.manage_prefabs.get_unity_instance_from_context",
        AsyncMock(return_value="unity-instance-1"),
    )
    monkeypatch.setattr(
        "services.tools.manage_prefabs.send_with_unity_instance",
        fake_send,
    )
    monkeypatch.setattr(
        "services.tools.manage_prefabs.preflight",
        AsyncMock(return_value=None),
    )
    return captured


# ── component_properties ─────────────────────────────────────────────


class TestManagePrefabsComponentProperties:
    """Tests for the component_properties parameter on manage_prefabs."""

    def test_component_properties_parameter_exists(self):
        """The manage_prefabs tool should have a component_properties parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "component_properties" in sig.parameters

    def test_component_properties_parameter_is_optional(self):
        """component_properties should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["component_properties"]
        assert param.default is None

    def test_tool_description_mentions_component_properties(self):
        """The tool description should mention component_properties."""
        prefab_tool = next(
            (t for t in get_registered_tools() if t["name"] == "manage_prefabs"), None
        )
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "component_properties" in desc

    def test_required_params_include_modify_contents(self):
        """modify_contents should be a valid action requiring prefab_path."""
        from services.tools.manage_prefabs import REQUIRED_PARAMS
        assert "modify_contents" in REQUIRED_PARAMS
        assert "prefab_path" in REQUIRED_PARAMS["modify_contents"]


# ── delete_child ─────────────────────────────────────────────────────


class TestManagePrefabsDeleteChild:
    """Tests for the delete_child parameter on manage_prefabs."""

    def test_delete_child_parameter_exists(self):
        """The manage_prefabs tool should have a delete_child parameter."""
        sig = inspect.signature(manage_prefabs)
        assert "delete_child" in sig.parameters

    def test_delete_child_parameter_is_optional(self):
        """delete_child should default to None."""
        sig = inspect.signature(manage_prefabs)
        param = sig.parameters["delete_child"]
        assert param.default is None

    def test_tool_description_mentions_delete_child(self):
        """The tool description should mention delete_child."""
        prefab_tool = next(
            (t for t in get_registered_tools() if t["name"] == "manage_prefabs"), None
        )
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "delete_child" in desc

    def test_delete_child_string_forwards_to_unity(self, mock_unity):
        """A single string delete_child should be forwarded as-is."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
                delete_child="Child1",
            )
        )
        assert result["success"] is True
        assert mock_unity["tool_name"] == "manage_prefabs"
        assert mock_unity["params"]["deleteChild"] == "Child1"

    def test_delete_child_list_forwards_to_unity(self, mock_unity):
        """A list of delete_child paths should be forwarded as-is."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
                delete_child=["Child1", "Child2/Grandchild"],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["deleteChild"] == ["Child1", "Child2/Grandchild"]

    def test_delete_child_none_omitted_from_params(self, mock_unity):
        """When delete_child is None, deleteChild should not appear in params."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
            )
        )
        assert "deleteChild" not in mock_unity["params"]


# ── Prefab Stage Actions ────────────────────────────────────────────


class TestManagePrefabsStageActions:
    """Tests for open/save/close prefab stage actions on manage_prefabs."""

    def test_description_mentions_open_prefab_stage(self):
        """The tool description should mention open_prefab_stage."""
        prefab_tool = next(
            (t for t in get_registered_tools() if t["name"] == "manage_prefabs"), None
        )
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "open_prefab_stage" in desc

    def test_description_mentions_save_prefab_stage(self):
        """The tool description should mention save_prefab_stage."""
        prefab_tool = next(
            (t for t in get_registered_tools() if t["name"] == "manage_prefabs"), None
        )
        assert prefab_tool is not None
        desc = prefab_tool.get("description") or prefab_tool.get("kwargs", {}).get("description", "")
        assert "save_prefab_stage" in desc

    def test_open_prefab_stage_forwards_prefab_path(self, mock_unity):
        """open_prefab_stage should forward prefab_path as prefabPath."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="open_prefab_stage",
                prefab_path="Assets/Prefabs/Test.prefab",
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["action"] == "open_prefab_stage"
        assert mock_unity["params"]["prefabPath"] == "Assets/Prefabs/Test.prefab"
        assert mock_unity["tool_name"] == "manage_prefabs"

    def test_open_prefab_stage_requires_prefab_path(self, mock_unity):
        """open_prefab_stage should fail without prefab_path."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="open_prefab_stage",
            )
        )
        assert result["success"] is False
        assert "prefab_path" in result["message"]

    def test_save_prefab_stage_forwards_to_unity(self, mock_unity):
        """save_prefab_stage should forward to Unity."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="save_prefab_stage",
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["action"] == "save_prefab_stage"
        assert mock_unity["tool_name"] == "manage_prefabs"

    def test_close_prefab_stage_forwards_to_unity(self, mock_unity):
        """close_prefab_stage should forward to Unity."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="close_prefab_stage",
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["action"] == "close_prefab_stage"
        assert mock_unity["tool_name"] == "manage_prefabs"


# ── components / properties / component shorthand ────────────────────


class TestManagePrefabsComponentsAndProperties:
    """Tests for the components/properties/component parameters (Phase 3d re-apply)."""

    def test_components_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "components" in sig.parameters

    def test_properties_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "properties" in sig.parameters

    def test_component_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "component" in sig.parameters

    def test_components_bool_true_forwarded(self, mock_unity):
        """components=True should be forwarded as a bool."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
                components=True,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["components"] is True

    def test_components_list_forwarded(self, mock_unity):
        """components=['Rigidbody', 'Light'] should be forwarded as a list."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
                components=["Rigidbody", "Light"],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["components"] == ["Rigidbody", "Light"]

    def test_components_string_true_forwarded_as_bool(self, mock_unity):
        """components='true' (string) should be forwarded as bool True."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
                components="true",
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["components"] is True

    def test_components_json_string_parsed(self, mock_unity):
        """components='["Rigidbody"]' (JSON string) should be parsed to a list."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
                components='["Rigidbody"]',
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["components"] == ["Rigidbody"]

    def test_components_none_not_forwarded(self, mock_unity):
        """components=None should not add a components key to params."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
            )
        )
        assert "components" not in mock_unity["params"]

    def test_properties_list_forwarded_for_hierarchy(self, mock_unity):
        """properties=['sizeDelta', 'anchoredPosition'] should be forwarded as a list."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                components=["RectTransform"],
                properties=["sizeDelta", "anchoredPosition"],
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["properties"] == ["sizeDelta", "anchoredPosition"]
        assert mock_unity["params"]["components"] == ["RectTransform"]

    def test_properties_dict_forwarded_for_modify(self, mock_unity):
        """properties={'mass': 5.0} dict should be forwarded for modify_contents."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
                component="Rigidbody",
                properties={"mass": 5.0},
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["properties"] == {"mass": 5.0}
        assert mock_unity["params"]["component"] == "Rigidbody"

    def test_properties_none_not_forwarded(self, mock_unity):
        """properties=None should not add a properties key to params."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
            )
        )
        assert "properties" not in mock_unity["params"]

    def test_properties_json_string_parsed(self, mock_unity):
        """properties='["sizeDelta"]' (JSON string) should be parsed to a list."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                components=["RectTransform"],
                properties='["sizeDelta"]',
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["properties"] == ["sizeDelta"]


# ── include_internal / pagination ───────────────────────────────────


class TestManagePrefabsIncludeInternalAndPaging:
    """Tests for include_internal + pagination parameters (Phase 3d re-apply)."""

    def test_include_internal_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "include_internal" in sig.parameters

    def test_page_size_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "page_size" in sig.parameters

    def test_cursor_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "cursor" in sig.parameters

    def test_max_depth_parameter_exists(self):
        sig = inspect.signature(manage_prefabs)
        assert "max_depth" in sig.parameters

    def test_include_internal_true_forwarded(self, mock_unity):
        """include_internal=True should be forwarded as includeInternal=True."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                include_internal=True,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["includeInternal"] is True

    def test_include_internal_false_forwarded(self, mock_unity):
        """include_internal=False should be forwarded as includeInternal=False."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_info",
                prefab_path="Assets/Prefabs/Test.prefab",
                include_internal=False,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["includeInternal"] is False

    def test_include_internal_none_not_forwarded(self, mock_unity):
        """include_internal=None should not add the key to params."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
            )
        )
        assert "includeInternal" not in mock_unity["params"]

    def test_include_internal_not_forwarded_for_modify(self, mock_unity):
        """include_internal should NOT be forwarded for modify_contents (read-only axis)."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
                include_internal=True,
            )
        )
        assert "includeInternal" not in mock_unity["params"]

    def test_page_size_forwarded_for_hierarchy(self, mock_unity):
        """page_size should be forwarded as an int for get_hierarchy."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                page_size=50,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["page_size"] == 50

    def test_cursor_forwarded_for_hierarchy(self, mock_unity):
        """cursor should be forwarded as an int for get_hierarchy."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                cursor=200,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["cursor"] == 200

    def test_max_depth_forwarded_for_hierarchy(self, mock_unity):
        """max_depth should be forwarded as an int for get_hierarchy."""
        result = asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="get_hierarchy",
                prefab_path="Assets/Prefabs/Test.prefab",
                max_depth=10,
            )
        )
        assert result["success"] is True
        assert mock_unity["params"]["max_depth"] == 10

    def test_pagination_not_forwarded_for_modify(self, mock_unity):
        """page_size/cursor/max_depth should NOT be forwarded for modify_contents."""
        asyncio.run(
            manage_prefabs(
                SimpleNamespace(),
                action="modify_contents",
                prefab_path="Assets/Prefabs/Test.prefab",
                page_size=50,
                cursor=10,
                max_depth=5,
            )
        )
        assert "page_size" not in mock_unity["params"]
        assert "cursor" not in mock_unity["params"]
        assert "max_depth" not in mock_unity["params"]
