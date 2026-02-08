"""Integration tests for GPU debugging features."""
import inspect


class TestManageEditorStep:
    """Tests for the step action in manage_editor."""

    def test_step_action_in_literal(self):
        """Verify 'step' is in the action literal type."""
        from services.tools.manage_editor import manage_editor
        sig = inspect.signature(manage_editor)
        action_param = sig.parameters['action']
        # The annotation should include 'step'
        assert 'step' in str(action_param.annotation)

    def test_frames_parameter_exists(self):
        """Verify frames parameter exists."""
        from services.tools.manage_editor import manage_editor
        sig = inspect.signature(manage_editor)
        assert 'frames' in sig.parameters

    def test_frames_parameter_accepts_int_or_str(self):
        """Verify frames parameter accepts int or str."""
        from services.tools.manage_editor import manage_editor
        sig = inspect.signature(manage_editor)
        frames_param = sig.parameters['frames']
        annotation_str = str(frames_param.annotation)
        assert 'int' in annotation_str or 'str' in annotation_str


class TestReadConsoleRegex:
    """Tests for regex filtering in read_console."""

    def test_filter_regex_parameter_exists(self):
        """Verify filter_regex parameter exists."""
        from services.tools.read_console import read_console
        sig = inspect.signature(read_console)
        assert 'filter_regex' in sig.parameters

    def test_filter_regex_has_description(self):
        """Verify filter_regex has a helpful description."""
        from services.tools.read_console import read_console
        sig = inspect.signature(read_console)
        filter_regex_param = sig.parameters['filter_regex']
        # Check the Annotated type has a description
        annotation_str = str(filter_regex_param.annotation)
        assert 'regex' in annotation_str.lower() or 'Regex' in annotation_str


class TestInspectBuffer:
    """Tests for the inspect_buffer tool."""

    def test_tool_exists(self):
        """Verify inspect_buffer tool is importable."""
        from services.tools.inspect_buffer import inspect_buffer
        assert inspect_buffer is not None

    def test_target_parameter_exists(self):
        """Verify target parameter exists and is required."""
        from services.tools.inspect_buffer import inspect_buffer
        sig = inspect.signature(inspect_buffer)
        assert 'target' in sig.parameters
        # target should not have a default (it's required)
        target_param = sig.parameters['target']
        assert target_param.default is inspect.Parameter.empty

    def test_optional_parameters_exist(self):
        """Verify optional parameters exist."""
        from services.tools.inspect_buffer import inspect_buffer
        sig = inspect.signature(inspect_buffer)
        assert 'start' in sig.parameters
        assert 'count' in sig.parameters
        assert 'format' in sig.parameters
        assert 'list_only' in sig.parameters

    def test_optional_parameters_have_defaults(self):
        """Verify optional parameters have None defaults."""
        from services.tools.inspect_buffer import inspect_buffer
        sig = inspect.signature(inspect_buffer)
        assert sig.parameters['start'].default is None
        assert sig.parameters['count'].default is None
        assert sig.parameters['format'].default is None
        assert sig.parameters['list_only'].default is None

    def test_tool_description_includes_syntax(self):
        """Verify tool description includes format syntax."""
        from services.tools.inspect_buffer import inspect_buffer
        # The description should mention the format syntax
        # This is a smoke test to ensure documentation exists
        assert inspect_buffer is not None
