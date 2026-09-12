import PropTypes from 'prop-types';
import React, { Component } from 'react';
import FileDetailsModal from 'BookFile/FileDetailsModal';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ChangeBookModal from './ChangeBookModal';
import styles from './BookFileActionsCell.css';

class BookFileActionsCell extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false,
      isConfirmDeleteModalOpen: false,
      isChangeBookModalOpen: false
    };
  }

  //
  // Listeners

  onDetailsPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onChangeBookPress = () => {
    this.setState({ isChangeBookModalOpen: true });
  };

  onChangeBookModalClose = () => {
    this.setState({ isChangeBookModalOpen: false });
  };

  onDeleteFilePress = () => {
    this.setState({ isConfirmDeleteModalOpen: true });
  };

  onConfirmDelete = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
    this.props.deleteBookFile({ id: this.props.id });
  };

  onConfirmDeleteModalClose = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
  };

  //
  // Render

  render() {

    const {
      id,
      authorId,
      bookId,
      path
    } = this.props;

    const {
      isDetailsModalOpen,
      isConfirmDeleteModalOpen,
      isChangeBookModalOpen
    } = this.state;

    return (
      <TableRowCell className={styles.TrackActionsCell}>
        {
          path &&
            <IconButton
              name={icons.INFO}
              onPress={this.onDetailsPress}
            />
        }
        {
          path &&
            <IconButton
              name={icons.DOWNLOAD}
              title={translate('Download')}
              to={`${window.Readarr.apiRoot}/bookfile/${id}/download?apikey=${encodeURIComponent(window.Readarr.apiKey)}`}
              noRouter={true}
            />
        }
        {
          path && !!authorId &&
            <IconButton
              name={icons.EDIT}
              title={translate('ChangeBook')}
              onPress={this.onChangeBookPress}
            />
        }
        {
          path &&
            <IconButton
              name={icons.DELETE}
              onPress={this.onDeleteFilePress}
            />
        }

        <FileDetailsModal
          isOpen={isDetailsModalOpen}
          onModalClose={this.onDetailsModalClose}
          id={id}
        />

        <ChangeBookModal
          isOpen={isChangeBookModalOpen}
          authorId={authorId}
          bookFileIds={[id]}
          currentBookId={bookId}
          onModalClose={this.onChangeBookModalClose}
        />

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteBookFile')}
          message={translate('DeleteBookFileMessageText', [path])}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDelete}
          onCancel={this.onConfirmDeleteModalClose}
        />
      </TableRowCell>

    );
  }
}

BookFileActionsCell.propTypes = {
  id: PropTypes.number.isRequired,
  authorId: PropTypes.number,
  bookId: PropTypes.number,
  path: PropTypes.string,
  deleteBookFile: PropTypes.func.isRequired
};

export default BookFileActionsCell;
