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
    this.props.onBookSelect(this.props.id);
  };

  //
  // Render

  render() {
    const {
      title,
      seriesTitle,
      releaseDate,
      isCurrent
    } = this.props;

    return (
      <TableRowButton onPress={this.onPress}>
        <TableRowCell className={styles.title}>
          {title}
          {
            seriesTitle ?
              <span className={styles.series}>
                {seriesTitle}
              </span> :
              null
          }
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
  seriesTitle: PropTypes.string,
  releaseDate: PropTypes.string,
  isCurrent: PropTypes.bool,
  onBookSelect: PropTypes.func.isRequired
};

export default ChangeBookRow;
