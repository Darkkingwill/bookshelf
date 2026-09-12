import PropTypes from 'prop-types';
import React, { Component } from 'react';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRowButton from 'Components/Table/TableRowButton';
import translate from 'Utilities/String/translate';
import styles from './ChangeBookRow.css';

class ChangeBookRow extends Component {

  //
  // Listeners

  onPress = () => {
    const {
      id,
      editions
    } = this.props;

    // A book's files hang off an edition, not the book, so resolve which edition to
    // attach to. Prefer the monitored one - there is normally exactly one - and fall
    // back to the first so a book with no monitored edition is still selectable.
    const monitored = (editions || []).find((e) => e.monitored);
    const edition = monitored || (editions || [])[0];

    this.props.onBookSelect(id, edition ? edition.id : 0);
  };

  //
  // Render

  render() {
    const {
      title,
      releaseDate,
      isCurrent
    } = this.props;

    return (
      <TableRowButton onPress={this.onPress}>
        <TableRowCell className={styles.title}>
          {title}
          {
            isCurrent ?
              <span className={styles.current}>
                {translate('CurrentlyFiledHere')}
              </span> :
              null
          }
        </TableRowCell>

        <RelativeDateCellConnector
          date={releaseDate}
        />
      </TableRowButton>
    );
  }
}

ChangeBookRow.propTypes = {
  id: PropTypes.number.isRequired,
  title: PropTypes.string.isRequired,
  releaseDate: PropTypes.string,
  editions: PropTypes.arrayOf(PropTypes.object),
  isCurrent: PropTypes.bool,
  onBookSelect: PropTypes.func.isRequired
};

export default ChangeBookRow;
