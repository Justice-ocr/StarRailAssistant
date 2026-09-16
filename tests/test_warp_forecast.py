from unittest.mock import Mock

from extensions.warp_forecast import WarpForecastConfig, WarpForecastExtension, _find_top_bar_jade


def test_missing_jade_is_not_zero():
    assert _find_top_bar_jade([]) is None
    assert _find_top_bar_jade([([[0, 0], [10, 0], [10, 10], [0, 10]], "0", 0.99)]) == 0


def test_failed_scan_does_not_report_success():
    extension = WarpForecastExtension.__new__(WarpForecastExtension)
    extension.config = WarpForecastConfig()
    extension._ensure_game_world_ready = Mock(return_value=True)
    extension._read_bag_resources = Mock(side_effect=RuntimeError("scan failed"))
    extension._return_to_world = Mock()
    extension.send_notification = Mock()
    assert extension.run() is False
    assert extension.send_notification.call_args.args[1] == "error"
    extension._return_to_world.assert_called_once()


def test_pass_scan_uses_relative_coordinates():
    extension = WarpForecastExtension.__new__(WarpForecastExtension)
    extension.operator = Mock(spec=["click_point", "stop_event"])
    extension.operator.stop_event = None
    extension._read_detail_title = Mock(side_effect=["星轨专票", "星轨通票"])
    extension._read_detail_count = Mock(side_effect=[5, 0])
    assert extension._scan_passes() == (5, 0)
    for call in extension.operator.click_point.call_args_list:
        assert all(isinstance(value, float) and 0 < value < 1 for value in call.args)


def test_missing_pass_is_not_treated_as_zero():
    extension = WarpForecastExtension.__new__(WarpForecastExtension)
    extension.operator = Mock(spec=["click_point", "stop_event"])
    extension.operator.stop_event = None
    extension._read_detail_title = Mock(return_value="")
    extension._read_detail_count = Mock()
    try:
        extension._scan_passes()
    except RuntimeError as exc:
        assert "未完整识别" in str(exc)
    else:
        raise AssertionError("missing passes must fail the scan")
