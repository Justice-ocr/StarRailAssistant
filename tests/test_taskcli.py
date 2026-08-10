import cmd2

from tasks.taskcli import CurrencyWarsCommands, TrailblazePowerCommands


def test_task_command_sets_are_runtime_compatible():
    assert issubclass(TrailblazePowerCommands, cmd2.CommandSet)
    assert issubclass(CurrencyWarsCommands, cmd2.CommandSet)


def test_task_command_sets_are_auto_registered():
    cli = cmd2.Cmd(auto_load_commands=True)

    assert "tpconfig" in cli.get_all_commands()
    assert "strategy" in cli.get_all_commands()
